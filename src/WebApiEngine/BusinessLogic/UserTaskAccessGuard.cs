using Model;

namespace WebApiEngine.BusinessLogic;

/// <summary>Aktive Task-/Token-/Instanzbindung unter dem bestehenden Lifecyclelock. Aufrufer hält zusätzlich die Engine-Sperre.</summary>
internal static class UserTaskAccessGuard
{
    internal static async Task<ExtendedUserTaskSubscription?> LoadCurrentAsync(IStorageSystem storage, Guid taskId)
    {
        try { if (!await storage.UserTaskLifecycleStorage.LockTask(taskId)) return null; }
        catch (NotSupportedException) { /* Einzelprozess-Dateiablage bleibt durch die Engine-Sperre serialisiert. */ }
        var task = await storage.SubscriptionStorage.GetUserTaskExtended(taskId);
        if (task?.ProcessInstanceId is not { } instanceId
            || task.Token is not { State: FlowNodeState.Active, CurrentFlowNode: BPMN.HumanInteraction.UserTask }) return null;
        ProcessInstanceInfo instance;
        try { instance = await storage.InstanceStorage.GetProcessInstance(instanceId); }
        catch (FileNotFoundException) { return null; }
        var tokens = instance.Tokens.Where(token => token.Id == task.Token.Id).Take(2).ToArray();
        return tokens.Length == 1
            && tokens[0] is { State: FlowNodeState.Active, CurrentFlowNode: BPMN.HumanInteraction.UserTask current }
            && current.Id == task.Token.CurrentFlowNode.Id && tokens[0].ProcessInstanceId == task.Token.ProcessInstanceId
            && instance.InstanceId == instanceId && instance.DefinitionId == task.DefinitionId
            && instance.metaDefinitionId == task.MetaDefinitionId && instance.ProcessId == task.ProcessId ? task : null;
    }
}
