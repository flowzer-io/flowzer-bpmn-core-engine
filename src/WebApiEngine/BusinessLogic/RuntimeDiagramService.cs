using Model;
using StorageSystem;
using StorageSystem.Exceptions;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Objektberechtigter Read-Anwendungsfall für die datensparsame technische Laufzeitsicht.
/// Instanz, gebundene Definition und Ereignisse werden aus derselben Storage-Sicht gelesen.
/// </summary>
public sealed class RuntimeDiagramService(
    ITransactionalStorageProvider storageProvider,
    InstanceAccessService instanceAccess,
    TimeProvider timeProvider)
{
    public async Task<RuntimeDiagramDto?> GetAsync(Guid instanceId)
    {
        if (!await instanceAccess.CanInspectAsync(instanceId)) return null;

        using var storage = storageProvider.GetTransactionalStorage();
        ProcessInstanceInfo instance;
        string xml;
        try
        {
            instance = await storage.InstanceStorage.GetProcessInstance(instanceId);
            _ = await storage.DefinitionStorage.GetDefinitionById(instance.DefinitionId);
            xml = await storage.DefinitionStorage.GetBinary(instance.DefinitionId);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DefinitionStorageNotFoundException)
        {
            return null;
        }

        var diagramXml = RuntimeDiagramXmlSanitizer.Sanitize(xml, instance.ProcessId);
        if (diagramXml is null) return null;

        IReadOnlyList<RuntimeNodeEvent> events;
        try
        {
            events = await storage.RuntimeNodeEventStorage.GetByProcessInstance(instanceId);
        }
        catch (NotSupportedException)
        {
            events = [];
        }

        var orderedEvents = events
            .Where(item => item.DefinitionId == instance.DefinitionId)
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.TokenId)
            .ThenBy(item => item.Id)
            .ToArray();

        return new RuntimeDiagramDto
        {
            InstanceId = instance.InstanceId,
            DefinitionId = instance.DefinitionId,
            ProcessId = instance.ProcessId,
            State = (ProcessInstanceStateDto)instance.State,
            SnapshotAtUtc = timeProvider.GetUtcNow(),
            DiagramXml = diagramXml,
            Nodes = AggregateNodes(orderedEvents),
            Events = orderedEvents.Select(item => new RuntimeNodeEventDto
            {
                Id = item.Id,
                FlowNodeId = item.FlowNodeId,
                State = (FlowNodeStateDto)item.State,
                OccurredAtUtc = item.OccurredAtUtc
            }).ToArray()
        };
    }

    private static RuntimeNodeSummaryDto[] AggregateNodes(IEnumerable<RuntimeNodeEvent> events)
    {
        // Pro Token und Knoten zählt nur der jüngste persistierte Zustand. Derselbe Token
        // kann über einen Loop denselben Knoten erneut erreichen, ohne dass alte Fakten
        // überschrieben werden.
        return events
            .GroupBy(item => new { item.TokenId, item.FlowNodeId })
            .Select(group => group
                .OrderByDescending(item => item.OccurredAtUtc)
                .ThenByDescending(item => item.Id)
                .First())
            .GroupBy(item => item.FlowNodeId, StringComparer.Ordinal)
            .Select(group => new RuntimeNodeSummaryDto
            {
                FlowNodeId = group.Key,
                Status = group.Select(item => ToStatus(item.State))
                    .OrderByDescending(StatusPrecedence)
                    .First(),
                TokenCount = group.Select(item => item.TokenId).Distinct().Count(),
                LastChangedAtUtc = group.Max(item => item.OccurredAtUtc)
            })
            .OrderBy(item => item.LastChangedAtUtc)
            .ThenBy(item => item.FlowNodeId, StringComparer.Ordinal)
            .ToArray();
    }

    private static RuntimeNodeStatusDto ToStatus(FlowNodeState state) => state switch
    {
        FlowNodeState.Failing or FlowNodeState.Failed => RuntimeNodeStatusDto.Failed,
        FlowNodeState.Terminating or FlowNodeState.Terminated or FlowNodeState.Withdrawn => RuntimeNodeStatusDto.Cancelled,
        FlowNodeState.Completed or FlowNodeState.Compensated or FlowNodeState.Merged => RuntimeNodeStatusDto.Completed,
        _ => RuntimeNodeStatusDto.Active
    };

    private static int StatusPrecedence(RuntimeNodeStatusDto status) => status switch
    {
        RuntimeNodeStatusDto.Failed => 4,
        RuntimeNodeStatusDto.Active => 3,
        RuntimeNodeStatusDto.Cancelled => 2,
        RuntimeNodeStatusDto.Completed => 1,
        _ => 0
    };
}
