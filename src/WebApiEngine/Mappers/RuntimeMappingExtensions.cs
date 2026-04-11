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
            CurrentFlowNodeId = token.CurrentFlowNode?.Id,
            CurrentFlowElement = token.CurrentFlowNode?.ToExpando(),
            Variables = token.Variables,
            OutputData = token.OutputData,
            PreviousTokenId = token.PreviousToken?.Id,
            ParentTokenId = token.ParentTokenId
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
            ProcessInstanceId = subscription.ProcessInstanceId,
            DefinitionId = subscription.DefinitionId,
            ProcessId = subscription.ProcessId
        };
    }

    public static ExtendedUserTaskSubscriptionDto ToDto(this ExtendedUserTaskSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return new ExtendedUserTaskSubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Token = subscription.Token.ToDto(),
            UserCandidates = [.. subscription.UserCandidates],
            UserGroups = [.. subscription.UserGroups],
            CurrenAssignedUser = subscription.CurrenAssignedUser,
            ProcessInstanceId = subscription.ProcessInstanceId,
            DefinitionId = subscription.DefinitionId,
            ProcessId = subscription.ProcessId,
            DefinitionMetaName = subscription.DefinitionMetaName,
            DefinitionVersion = subscription.DefinitionVersion.ToDto()
        };
    }
}
