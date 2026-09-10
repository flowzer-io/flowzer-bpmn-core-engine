using WebApiEngine.Auth;
using WebApiEngine.Idempotency;

namespace WebApiEngine.BusinessLogic;

public partial class BpmnBusinessLogic
{
    /// <summary>
    /// Prüft Aufgabenidentität und Zuweisung im selben geschützten Read-Modify-Write-Zyklus
    /// wie den Abschluss. Der Aufrufer muss einen serverseitig ermittelten Benutzerkontext
    /// und gegebenenfalls das explizit geprüfte Betriebsrecht übergeben.
    /// </summary>
    public async Task<UserTaskCompletionOutcome> CompleteUserTaskAsync(
        UserTaskResult result,
        CurrentUserContext currentUser,
        bool canOperateAllTasks = false,
        CancellationToken cancellationToken = default,
        IdempotencyRequest? idempotency = null)
    {
        var userId = currentUser.RequireResolvedUserId("completing user tasks");
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("A non-empty user identity is required for completing user tasks.");
        }

        if (result.ProcessInstanceId is not { } instanceId)
        {
            throw new ArgumentException("User task results require a ProcessInstanceId.", nameof(result.ProcessInstanceId));
        }

        await _engineMutationLock.WaitAsync(cancellationToken);
        try
        {
            using var storage = storageProvider.GetTransactionalStorage();
            var acquisition = await IdempotencyExecution.Acquire(storage, idempotency);
            if (acquisition.IsReplay) return UserTaskCompletionOutcome.Completed;
            async Task<UserTaskCompletionOutcome> NotFound()
            {
                await IdempotencyExecution.Abandon(storage, acquisition);
                return UserTaskCompletionOutcome.NotFound;
            }
            var persistedMutationMayExist = false;
            try
            {
                var subscriptions = (await storage.SubscriptionStorage.GetAllUserTasks(instanceId))
                    .Where(task => task.Token?.Id == result.TokenId).ToArray();

                // Auch fehlende oder doppelte Subscriptions sind keine Erlaubnis. Es werden nur
                // Aufgaben dieser Instanz geladen, nicht der gesamte angereicherte Benutzerbestand.
                if (subscriptions.Length != 1)
                {
                    return await NotFound();
                }

                var subscription = subscriptions[0];
                bool taskLocked;
                try { taskLocked = await storage.UserTaskLifecycleStorage.LockTask(subscription.Id); }
                catch (NotSupportedException) { taskLocked = true; }
                if (!taskLocked)
                {
                    return await NotFound();
                }
                subscriptions = (await storage.SubscriptionStorage.GetAllUserTasks(instanceId))
                    .Where(task => task.Id == subscription.Id && task.Token?.Id == result.TokenId).ToArray();
                if (subscriptions.Length != 1) return await NotFound();
                subscription = subscriptions[0];
                if (subscription.ProcessInstanceId != instanceId
                    || subscription.Token.State != FlowNodeState.Active
                    || !string.Equals(subscription.Token.CurrentFlowNode?.Id, result.FlowNodeId, StringComparison.Ordinal))
                {
                    return await NotFound();
                }

                var access = await UserTaskWorkAuthorization.EvaluateAsync(
                    storage, subscription, currentUser, canOperateAllTasks);
                if (!access.CanWork)
                {
                    return await NotFound();
                }
                if (result.ExpectedTaskRevision is { } expectedTaskRevision
                    && expectedTaskRevision != (access.State?.Revision ?? 0))
                    throw new UserTaskLifecycleConflictException(
                        expectedTaskRevision, access.State?.Revision ?? 0);

                ProcessInstanceInfo processInstance;
                try
                {
                    processInstance = await storage.InstanceStorage.GetProcessInstance(instanceId);
                }
                catch (FileNotFoundException)
                {
                    // Verwaiste Subscription und unbekannte Aufgabe haben denselben Außenvertrag.
                    return await NotFound();
                }

                var instance = new InstanceEngine(processInstance.Tokens) { InstanceId = instanceId };
                var activeTokens = instance.GetActiveUserTasks().Where(token => token.Id == result.TokenId).ToArray();
                // Historische Engine-Tokens tragen eine eigene ProcessInstanceId, die nicht der
                // persistierten Instanz-ID entspricht. Maßgeblich ist daher die tatsächliche
                // Mitgliedschaft in den geladenen Instanz-Tokens, nicht dieses Legacy-Feld.
                if (activeTokens.Length != 1
                    || processInstance.InstanceId != instanceId
                    || activeTokens[0].ProcessInstanceId != subscription.Token.ProcessInstanceId
                    || !string.Equals(activeTokens[0].CurrentFlowNode?.Id, result.FlowNodeId, StringComparison.Ordinal)
                    || processInstance.DefinitionId != subscription.DefinitionId
                    || processInstance.metaDefinitionId != subscription.MetaDefinitionId
                    || processInstance.ProcessId != subscription.ProcessId)
                {
                    return await NotFound();
                }

                var validated = await ValidateFormInputAsync(storage,
                    (activeTokens[0].CurrentFlowNode as BPMN.HumanInteraction.UserTask)?.Implementation,
                    processInstance.DefinitionId, result.Data,
                    WebApiEngine.Forms.TaskFormContext.Read(processInstance.Tokens, activeTokens[0]),
                    result.ActionId, allowActions: true);
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var currentState = access.State;
                    var terminalState = new UserTaskWorkState
                    {
                        UserTaskId = subscription.Id,
                        Revision = checked((currentState?.Revision ?? 0) + 1),
                        UpdatedAtUtc = now
                    };
                    var terminalEvent = new UserTaskAssignmentEvent
                    {
                        Id = Guid.NewGuid(),
                        UserTaskId = subscription.Id,
                        ProcessInstanceId = subscription.ProcessInstanceId,
                        DefinitionId = subscription.DefinitionId,
                        ProcessId = subscription.ProcessId,
                        FlowNodeId = result.FlowNodeId,
                        Revision = terminalState.Revision,
                        Action = "complete",
                        ActorOwnerKey = UserTaskDraftOwnerKey.Create(currentUser),
                        ActorUserId = userId,
                        ActorDisplayName = currentUser.Names.FirstOrDefault(name => !Guid.TryParse(name, out _)),
                        PreviousDirectoryAssigneeUserId = currentState?.DirectoryAssigneeUserId,
                        PreviousAssigneeUserId = currentState?.AssigneeUserId,
                        PreviousAssigneeDisplayName = currentState?.AssigneeDisplayName,
                        Reason = "Aufgabe abgeschlossen",
                        CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString()
                                        ?? Guid.NewGuid().ToString("N"),
                        OccurredAtUtc = now
                    };
                    var lifecycle = await storage.UserTaskLifecycleStorage.TryWrite(
                        terminalState, currentState?.Revision ?? 0, terminalEvent);
                    if (lifecycle.Status != UserTaskLifecycleWriteStatus.Written)
                        return await NotFound();
                }
                catch (NotSupportedException)
                {
                    // Externe Legacy-Adapter bleiben bis zu ihrer Lifecycle-Erweiterung kompatibel.
                }
                instance.HandleTaskResult(result.TokenId, validated, userId);
                // SaveInstance schreibt bei der Dateiablage mehrere dauerhafte Dokumente.
                // Scheitert danach der Ergebnisdatensatz, bleibt der Ausgang absichtlich
                // unklar und der Idempotenzschlüssel für weitere Ausführung gesperrt.
                persistedMutationMayExist = true;
                await SaveInstance(storage, instance, processInstance.metaDefinitionId, processInstance.DefinitionId, processInstance.ProcessId);
                if (acquisition.Record is not null)
                    await storage.IdempotencyStorage.Complete(acquisition.Record.ScopeHash, null);
                storage.CommitChanges();
                return UserTaskCompletionOutcome.Completed;
            }
            catch
            {
                try { await IdempotencyExecution.Abandon(storage, acquisition, persistedMutationMayExist); }
                catch (Exception cleanupError)
                {
                    (logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BpmnBusinessLogic>.Instance)
                        .LogWarning(cleanupError, "Could not remove failed idempotency reservation {ScopeHash}.", acquisition.Record?.ScopeHash);
                }
                throw;
            }
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }
}
