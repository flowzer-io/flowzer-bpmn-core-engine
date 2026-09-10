using BPMN.HumanInteraction;
using Model;
using StorageSystem;

namespace WebApiEngine.Auth;

/// <summary>
/// Ressourcenrechte ohne HTTP-/Storage-Zugriffe. Besitzer oder aktuell berechtigte
/// Bearbeiter sehen nur eine Übersicht; Modellierungsrechte spielen hier keine Rolle.
/// </summary>
public static class InstanceAccessPolicy
{
    public static bool CanReadOverview(ProcessInstanceInfo instance, CurrentUserContext currentUser,
        IEnumerable<UserTaskSubscription> tasks, bool canInspect)
    {
        if (canInspect) return true;
        var masters = instance.Tokens.Where(token => token.ParentTokenId is null).Take(2).ToArray();
        if (currentUser.Identity is not null && masters.Length == 1
            && masters[0].Initiator == currentUser.Identity)
        {
            return true;
        }

        if (instance.IsFinished) return false;
        var identity = new UserTaskIdentity(currentUser.Names, currentUser.Groups);
        return tasks.Any(task => IsCurrentTask(instance, task) && IsAssigned(task, identity));
    }

    private static bool IsAssigned(UserTaskSubscription task, UserTaskIdentity identity)
    {
        UserTaskAssignment.EnsureAssignmentFromModel(task);
        return UserTaskAssignment.IsVisibleTo(task, identity, seeAll: false);
    }

    private static bool IsCurrentTask(ProcessInstanceInfo instance, UserTaskSubscription task) =>
        task.ProcessInstanceId == instance.InstanceId
        && task.DefinitionId == instance.DefinitionId
        && task.MetaDefinitionId == instance.metaDefinitionId
        && task.ProcessId == instance.ProcessId
        && task.Token is { State: FlowNodeState.Active, CurrentFlowNode: UserTask }
        && instance.Tokens.Any(token => token.Id == task.Token.Id
            && token is { State: FlowNodeState.Active, CurrentFlowNode: UserTask }
            && token.CurrentFlowNode.Id == task.Token.CurrentFlowNode.Id);
}
