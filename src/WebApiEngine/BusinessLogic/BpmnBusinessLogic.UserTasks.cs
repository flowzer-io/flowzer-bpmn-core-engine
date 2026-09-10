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
            if (subscription.ProcessInstanceId != instanceId
                || subscription.Token.State != FlowNodeState.Active
                || !string.Equals(subscription.Token.CurrentFlowNode?.Id, result.FlowNodeId, StringComparison.Ordinal))
            {
                return await NotFound();
            }

            UserTaskAssignment.EnsureAssignmentFromModel(subscription);
            var identity = new UserTaskIdentity(currentUser.Names, currentUser.Groups);
            if (!UserTaskAssignment.IsVisibleTo(subscription, identity, canOperateAllTasks))
            {
                return await NotFound();
            }

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
                processInstance.DefinitionId, result.Data, WebApiEngine.Forms.TaskFormContext.Read(processInstance.Tokens, activeTokens[0]));
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
