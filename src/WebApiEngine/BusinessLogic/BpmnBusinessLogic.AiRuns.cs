using System.Dynamic;
using BPMN.Activities;
using core_engine.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StorageSystem;
using WebApiEngine.Ai;

namespace WebApiEngine.BusinessLogic;

/// <summary>Zaehler eines atomaren Engine-Ergebnisdurchgangs.</summary>
internal sealed record AiRunEngineBatchResult(int Claimed, int Completed, int Incidents);

public partial class BpmnBusinessLogic
{
    /// <summary>
    /// Uebernimmt schema-validierte KI-Ergebnisse in dieselbe Storage-Transaktion wie den
    /// Prozessfortschritt. Ein PostgreSQL-Abbruch rollt deshalb Claim, Instanz und Laufstatus
    /// gemeinsam zurueck; die dateibasierte Ablage bleibt ausdruecklich Entwicklungsspeicher.
    /// </summary>
    internal async Task<AiRunEngineBatchResult> CompleteAiRunBatchAsync(
        TimeProvider timeProvider,
        AiRunExecutionPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var leaseOwner = $"engine:{Environment.ProcessId}:{Guid.NewGuid():N}";

        await _engineMutationLock.WaitAsync(cancellationToken);
        try
        {
            using var storage = storageProvider.GetTransactionalStorage();
            var claimed = await storage.AiRunStorage.ClaimResultRuns(
                leaseOwner,
                nowUtc,
                nowUtc + policy.LeaseDuration,
                policy.BatchSize);
            var completed = 0;
            var incidents = 0;

            foreach (var run in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var failureCode = await ValidateAndCompleteAiRunAsync(storage, run, cancellationToken);
                if (failureCode is null)
                {
                    var done = run with
                    {
                        Status = AiRunStatus.Completed,
                        LeaseOwner = null,
                        LeaseExpiresAtUtc = null,
                        FailureCode = null,
                        Revision = run.Revision + 1,
                        UpdatedAtUtc = nowUtc
                    };
                    var written = await storage.AiRunStorage.TryUpdate(
                        done,
                        run.Revision,
                        leaseOwner,
                        nowUtc);
                    if (written.Status != AiRunWriteStatus.Written)
                        throw new InvalidOperationException("The claimed AI result lease was lost before commit.");
                    completed++;
                }
                else
                {
                    var incident = run with
                    {
                        Status = AiRunStatus.Incident,
                        LeaseOwner = null,
                        LeaseExpiresAtUtc = null,
                        FailureCode = failureCode,
                        Revision = run.Revision + 1,
                        UpdatedAtUtc = nowUtc
                    };
                    var written = await storage.AiRunStorage.TryUpdate(
                        incident,
                        run.Revision,
                        leaseOwner,
                        nowUtc);
                    if (written.Status != AiRunWriteStatus.Written)
                        throw new InvalidOperationException("The claimed AI result lease was lost before incident commit.");
                    incidents++;
                }
            }

            storage.CommitChanges();
            return new AiRunEngineBatchResult(claimed.Count, completed, incidents);
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }

    private async Task<string?> ValidateAndCompleteAiRunAsync(
        ITransactionalStorage storage,
        AiRun run,
        CancellationToken cancellationToken)
    {
        ProcessInstanceInfo processInstance;
        try
        {
            processInstance = await storage.InstanceStorage.GetProcessInstance(run.ProcessInstanceId);
        }
        catch (FileNotFoundException)
        {
            return "ai.run.instance_not_found";
        }

        if (processInstance.DefinitionId != run.DefinitionId
            || processInstance.metaDefinitionId != run.MetaDefinitionId
            || processInstance.ProcessId != run.ProcessId)
            return "ai.run.instance_binding_mismatch";

        var instance = new core_engine.InstanceEngine(processInstance.Tokens)
        {
            InstanceId = processInstance.InstanceId
        };
        var token = instance.GetActiveServiceTasks().SingleOrDefault(candidate => candidate.Id == run.TokenId);
        if (token?.CurrentFlowNode is not ServiceTask { FlowzerAiTask: not null } task
            || !string.Equals(task.Id, run.FlowNodeId, StringComparison.Ordinal))
            return "ai.run.token_not_active";

        var definition = await storage.DefinitionStorage.GetDefinitionById(run.DefinitionId);
        if (definition.AiTaskBindings is null
            || !definition.AiTaskBindings.TryGetValue(task.Id, out var binding)
            || !MatchesBoundTask(run, task, binding))
            return "ai.run.binding_mismatch";

        ExpandoObject output;
        try
        {
            output = JsonConvert.DeserializeObject<ExpandoObject>(
                         run.OutputJson!,
                         new ExpandoObjectConverter())
                     ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return "ai.run.output_invalid";
        }

        try
        {
            // Kein menschlicher Akteur wird erfunden: CompletedByUserId und das Legacy-Feld
            // UserId bleiben fuer eine technische KI-Ausfuehrung null.
            instance.HandleTaskResult(token.Id, output);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FlowzerRuntimeException)
        {
            (logger ?? NullLogger<BpmnBusinessLogic>.Instance).LogWarning(
                "The result of AI run {AiRunId} could not be applied to its declared output mapping.",
                run.Id);
            return "ai.run.output_mapping_failed";
        }

        cancellationToken.ThrowIfCancellationRequested();
        await SaveInstance(
            storage,
            instance,
            processInstance.metaDefinitionId,
            processInstance.DefinitionId,
            processInstance.ProcessId);
        return null;
    }

    private static bool MatchesBoundTask(AiRun run, ServiceTask task, BoundAiTask binding)
    {
        var contract = task.FlowzerAiTask!;
        return binding.ConnectionId == run.ConnectionId
               && binding.ConnectionRevision == run.ConnectionRevision
               && binding.Model == run.Model
               && contract.ConnectionId == run.ConnectionId
               && contract.InstructionVersion == run.InstructionVersion
               && contract.Instruction == run.Instruction
               && contract.ResultSchema == run.ResultSchema
               && contract.MaxInputTokens == run.MaxInputTokens
               && contract.MaxOutputTokens == run.MaxOutputTokens
               && contract.TimeoutSeconds == run.TimeoutSeconds
               && (task.FlowzerRetries > 0 ? task.FlowzerRetries : 1) == run.MaximumAttempts;
    }
}
