using Flowzer.Shared;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>
/// Enthält Laufzeit-Mappings für Token und Subscription-Objekte.
/// </summary>
public static class RuntimeMappingExtensions
{
    public static TokenDto ToDto(this Token token, bool includeContext = true)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new TokenDto
        {
            Id = token.Id,
            State = (FlowNodeStateDto)token.State,
            CurrentFlowNodeId = token.CurrentFlowNode?.Id ?? string.Empty,
            CurrentFlowElement = includeContext ? token.CurrentFlowNode?.ToExpando() : null,
            Variables = includeContext ? token.Variables : null,
            OutputData = includeContext ? token.OutputData : null,
            PreviousTokenId = token.PreviousToken?.Id,
            ParentTokenId = token.ParentTokenId,
            CompletedByUserId = token.CompletedByUserId,
            StartTime = token.StartTime,
            LastStateChangeTime = token.LastStateChangeTime
        };
    }

    public static UserTaskSubscriptionDto ToDto(this UserTaskSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        UserTaskAssignment.EnsureAssignmentFromModel(subscription);

        return new UserTaskSubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Token = subscription.Token.ToDto(),
            UserCandidates = [.. subscription.UserCandidates],
            UserGroups = [.. subscription.UserGroups],
            CurrenAssignedUser = subscription.CurrenAssignedUser,
            Assignee = subscription.Assignee,
            CandidateUsers = [.. subscription.CandidateUsers],
            CandidateGroups = [.. subscription.CandidateGroups],
            AssignmentMode = AssignmentMode(subscription),
            DirectoryAssignee = UserSubject(subscription.DirectoryAssigneeUserId),
            DirectoryCandidateUsers = subscription.DirectoryCandidateUserIds.Select(id => UserSubject(id)).ToList(),
            DirectoryCandidateGroups = subscription.DirectoryCandidateGroupIds.Select(GroupSubject).ToList(),
            ProcessInstanceId = subscription.ProcessInstanceId,
            DefinitionId = subscription.DefinitionId,
            ProcessId = subscription.ProcessId
        };
    }

    public static ExtendedUserTaskSubscriptionDto ToDto(this ExtendedUserTaskSubscription subscription, bool includeTokenContext = true)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        UserTaskAssignment.EnsureAssignmentFromModel(subscription);

        // Der Form-Key und die Termine stehen nur am BPMN-Modellelement. Sie werden
        // hier flach in das DTO gehoben, damit Clients sie nicht aus dem dynamischen
        // Flow-Element herausparsen müssen (API-first).
        var userTask = subscription.Token.CurrentFlowNode as BPMN.HumanInteraction.UserTask;

        return new ExtendedUserTaskSubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Token = subscription.Token.ToDto(includeContext: includeTokenContext),
            UserCandidates = [.. subscription.UserCandidates],
            UserGroups = [.. subscription.UserGroups],
            CurrenAssignedUser = subscription.CurrenAssignedUser,
            Assignee = subscription.Assignee,
            CandidateUsers = [.. subscription.CandidateUsers],
            CandidateGroups = [.. subscription.CandidateGroups],
            AssignmentMode = AssignmentMode(subscription),
            DirectoryAssignee = UserSubject(subscription.DirectoryAssigneeUserId),
            DirectoryCandidateUsers = subscription.DirectoryCandidateUserIds.Select(id => UserSubject(id)).ToList(),
            DirectoryCandidateGroups = subscription.DirectoryCandidateGroupIds.Select(GroupSubject).ToList(),
            ProcessInstanceId = subscription.ProcessInstanceId,
            DefinitionId = subscription.DefinitionId,
            ProcessId = subscription.ProcessId,
            DefinitionMetaName = subscription.DefinitionMetaName,
            DefinitionVersion = subscription.DefinitionVersion.ToDto(),
            FormKey = userTask?.Implementation,
            DueDate = userTask?.FlowzerDueDate,
            FollowUpDate = userTask?.FlowzerFollowUpDate,
            Priority = userTask?.FlowzerPriority,
            WorkState = new UserTaskWorkStateDto
            {
                Revision = 0,
                Claimed = false,
                IsAssignedToCurrentUser = false,
                CanWork = true,
                CanClaim = true,
                CanRelease = false,
                CanAssign = false,
                CanDelegate = false
            }
        };
    }

    private static string AssignmentMode(UserTaskSubscription subscription) =>
        subscription.AssignmentMode == BPMN.HumanInteraction.UserTaskAssignmentMode.Directory
            ? "directory"
            : "text";

    private static SubjectRefDto? UserSubject(Guid? id) => id.HasValue
        ? new SubjectRefDto { Kind = "user", Id = id.Value }
        : null;

    private static SubjectRefDto UserSubject(Guid id) =>
        new() { Kind = "user", Id = id };

    private static SubjectRefDto GroupSubject(Guid id) =>
        new() { Kind = "group", Id = id };
}
