using Flowzer.Shared;
using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>
/// Enthält Laufzeit-Mappings für Token und Subscription-Objekte.
/// </summary>
public static class RuntimeMappingExtensions
{
    public static TokenDto ToDto(this Token token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return new TokenDto
        {
            Id = token.Id,
            State = (FlowNodeStateDto)token.State,
            CurrentFlowNodeId = token.CurrentFlowNode?.Id ?? string.Empty,
            CurrentFlowElement = token.CurrentFlowNode?.ToExpando(),
            Variables = token.Variables,
            OutputData = token.OutputData,
            PreviousTokenId = token.PreviousToken?.Id,
            ParentTokenId = token.ParentTokenId,
            StartTime = token.StartTime,
            LastStateChangeTime = token.LastStateChangeTime
        };
    }

    public static UserTaskSubscriptionDto ToDto(this UserTaskSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

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
            ProcessInstanceId = subscription.ProcessInstanceId,
            DefinitionId = subscription.DefinitionId,
            ProcessId = subscription.ProcessId
        };
    }

    public static ExtendedUserTaskSubscriptionDto ToDto(this ExtendedUserTaskSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        // Der Form-Key und die Termine stehen nur am BPMN-Modellelement. Sie werden
        // hier flach in das DTO gehoben, damit Clients sie nicht aus dem dynamischen
        // Flow-Element herausparsen müssen (API-first).
        var userTask = subscription.Token.CurrentFlowNode as BPMN.HumanInteraction.UserTask;

        return new ExtendedUserTaskSubscriptionDto
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
            ProcessInstanceId = subscription.ProcessInstanceId,
            DefinitionId = subscription.DefinitionId,
            ProcessId = subscription.ProcessId,
            DefinitionMetaName = subscription.DefinitionMetaName,
            DefinitionVersion = subscription.DefinitionVersion.ToDto(),
            FormKey = userTask?.Implementation,
            DueDate = userTask?.FlowzerDueDate,
            FollowUpDate = userTask?.FlowzerFollowUpDate,
            Priority = userTask?.FlowzerPriority
        };
    }
}
