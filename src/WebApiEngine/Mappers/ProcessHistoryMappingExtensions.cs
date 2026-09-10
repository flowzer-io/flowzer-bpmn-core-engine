using Model;
using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>Datensparsame Projektion des internen Human-Task-Audits.</summary>
public static class ProcessHistoryMappingExtensions
{
    public static ProcessHistoryEventDto ToHistoryDto(this UserTaskAssignmentEvent item) => new()
    {
        Id = item.Id,
        UserTaskId = item.UserTaskId,
        FlowNodeId = item.FlowNodeId,
        Action = item.Action,
        Revision = item.Revision,
        OccurredAtUtc = item.OccurredAtUtc
    };
}
