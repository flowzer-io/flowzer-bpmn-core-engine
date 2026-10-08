using WebApiEngine.Auth;

namespace WebApiEngine.BusinessLogic;

public partial class BpmnBusinessLogic
{
    /// <summary>
    /// Zieht einen laufenden direkten Start ausschließlich für dessen verifizierten
    /// Initiator zurück. Wiederholung bestätigt denselben Rückzug; kein Rollback
    /// bereits ausgeführter Schritte und kein Operator-/Namensersatz.
    /// </summary>
    public Task<ProcessInstanceInfo> WithdrawInstance(Guid instanceId, CurrentUserContext actor)
    {
        actor.RequireResolvedUserId("withdrawing an own instance");
        if (actor.UserId == Guid.Empty || actor.Identity is null)
            throw new UnauthorizedAccessException("A stable authenticated initiator is required.");
        return CancelInstanceCore(instanceId, actor);
    }

    private async Task<ProcessInstanceInfo> CancelInstanceCore(Guid instanceId, CurrentUserContext? withdrawingUser)
    {
        await _engineMutationLock.WaitAsync();
        try
        {
            using var storageSystem = storageProvider.GetTransactionalStorage();
            await storageSystem.InstanceStorage.LockForMutation(instanceId);
            var processInstance = await storageSystem.InstanceStorage.GetProcessInstance(instanceId);
            Token? master = null;
            if (withdrawingUser is not null)
            {
                var masters = processInstance.Tokens.Where(token => token.ParentTokenId is null).Take(2).ToArray();
                // Dieselbe Prüfung unter der Mutations-/Datenbanksperre wie der
                // Zustandswechsel. Auch Betriebsrecht eröffnet keinen fremden Rückzug.
                if (masters.Length != 1 || masters[0].Initiator != withdrawingUser.Identity
                    || processInstance.ParentInstanceId.HasValue || processInstance.ParentTokenId.HasValue
                    || masters[0].CallingInstanceId.HasValue || masters[0].CallingTokenId.HasValue)
                    throw new FileNotFoundException("The process instance was not found.");
                master = masters[0];
            }
            if (processInstance.IsFinished)
            {
                // Nur ein bereits erfolgreich ausgeführter persönlicher Rückzug ist
                // natürlich idempotent. Fachlicher Abschluss/Betriebsabbruch bleibt 409.
                if (master?.Withdrawal is not null && processInstance.State == ProcessInstanceState.Terminated)
                {
                    // Die Dateiablage ist nicht transaktional: Ein früherer Versuch kann
                    // den Parent bereits gespeichert haben, bevor Kindabbruch/History
                    // scheiterten. Ohne erneute idempotente Restarbeit wäre 200 unzutreffend.
                    var withdrawn = new InstanceEngine(processInstance.Tokens) { InstanceId = instanceId };
                    await PersistInstance(storageSystem, withdrawn, processInstance.metaDefinitionId,
                        processInstance.DefinitionId, processInstance.ProcessId);
                    await CancelCalledInstances(storageSystem, instanceId);
                    storageSystem.CommitChanges();
                    return processInstance;
                }
                throw new InstanceFinishedConflictException(
                    $"Process instance \"{instanceId}\" is already finished and cannot be cancelled.");
            }

            var instance = new InstanceEngine(processInstance.Tokens) { InstanceId = processInstance.InstanceId };
            instance.Cancel();
            if (master is not null)
                master.Withdrawal = new ProcessWithdrawal(withdrawingUser!.Identity!,
                    withdrawingUser.UserId, DateTimeOffset.UtcNow);

            await SaveInstance(storageSystem, instance, processInstance.metaDefinitionId,
                processInstance.DefinitionId, processInstance.ProcessId);
            // Der Elternabbruch ist zuerst gespeichert: Ein beendetes Kind darf
            // ihn danach nicht mehr fortsetzen. Bestehende Abbruchsemantik bleibt erhalten.
            await CancelCalledInstances(storageSystem, instanceId);
            storageSystem.CommitChanges();
            return CreateProcessInstanceInfo(processInstance.DefinitionId, processInstance.metaDefinitionId,
                processInstance.ProcessId, instance, processInstance.Migrations, processInstance.Modifications);
        }
        finally { _engineMutationLock.Release(); }
    }
}
