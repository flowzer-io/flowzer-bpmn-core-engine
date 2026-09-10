using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>
/// Bündelt das Mapping von gespeicherten Prozessinstanzen in API-DTOs.
/// </summary>
public static class ProcessInstanceMappingExtensions
{
    public static async Task<ProcessInstanceInfoDto> ToDtoAsync(
        this ProcessInstanceInfo processInstanceInfo,
        IDefinitionStorage definitionStorage,
        bool canInspect = true)
    {
        ArgumentNullException.ThrowIfNull(processInstanceInfo);
        ArgumentNullException.ThrowIfNull(definitionStorage);

        var metaNamesById = await GetMetaNamesByIdAsync(definitionStorage);
        return processInstanceInfo.ToDto(metaNamesById, canInspect);
    }

    public static async Task<List<ProcessInstanceInfoDto>> ToDtosAsync(
        this IEnumerable<ProcessInstanceInfo> processInstances,
        IDefinitionStorage definitionStorage,
        bool canInspect = true)
    {
        ArgumentNullException.ThrowIfNull(processInstances);
        ArgumentNullException.ThrowIfNull(definitionStorage);

        var metaNamesById = await GetMetaNamesByIdAsync(definitionStorage);
        return processInstances
            .Select(instance => instance.ToDto(metaNamesById, canInspect))
            .ToList();
    }

    private static async Task<Dictionary<string, string>> GetMetaNamesByIdAsync(IDefinitionStorage definitionStorage)
    {
        var metaDefinitions = await definitionStorage.GetAllMetaDefinitions();
        return metaDefinitions
            .GroupBy(metaDefinition => metaDefinition.DefinitionId)
            .ToDictionary(group => group.Key, group => group.First().Name);
    }

    private static ProcessInstanceInfoDto ToDto(
        this ProcessInstanceInfo processInstanceInfo,
        IReadOnlyDictionary<string, string> metaNamesById,
        bool canInspect)
    {
        // Instanzen ohne zugehörige Meta-Definition (z. B. nach einem Direkt-Deploy
        // an der Katalogpflege vorbei) dürfen die Instanzliste nicht zerstören —
        // dann bleibt die technische Definition-Id als Anzeigename sichtbar.
        var relatedDefinitionName = metaNamesById.TryGetValue(processInstanceInfo.metaDefinitionId, out var name)
            ? name
            : processInstanceInfo.metaDefinitionId;

        return new ProcessInstanceInfoDto
        {
            InstanceId = processInstanceInfo.InstanceId,
            DefinitionId = processInstanceInfo.DefinitionId,
            RelatedDefinitionId = processInstanceInfo.metaDefinitionId,
            RelatedDefinitionName = relatedDefinitionName,
            MessageSubscriptionCount = canInspect ? processInstanceInfo.MessageSubscriptionCount : 0,
            SignalSubscriptionCount = canInspect ? processInstanceInfo.SignalSubscriptionCount : 0,
            UserTaskSubscriptionCount = processInstanceInfo.UserTaskSubscriptionCount,
            ServiceSubscriptionCount = canInspect ? processInstanceInfo.ServiceSubscriptionCount : 0,
            State = (ProcessInstanceStateDto)processInstanceInfo.State,
            Tokens = canInspect ? processInstanceInfo.Tokens.Select(token => token.ToDto()).ToList() : [],
            CanInspect = canInspect,
            StartedAt = GetStartedAt(processInstanceInfo),
            FinishedAt = GetFinishedAt(processInstanceInfo)
        };
    }

    // Die Ablage speichert keinen eigenen Instanz-Zeitstempel. Start- und Endzeitpunkt
    // werden deshalb aus den Tokens abgeleitet: das älteste Token markiert den Start,
    // der letzte Statuswechsel einer beendeten Instanz deren Ende.
    private static DateTime? GetStartedAt(ProcessInstanceInfo processInstanceInfo)
    {
        return processInstanceInfo.Tokens.Count == 0
            ? null
            : processInstanceInfo.Tokens.Min(token => token.StartTime);
    }

    private static DateTime? GetFinishedAt(ProcessInstanceInfo processInstanceInfo)
    {
        if (!processInstanceInfo.IsFinished || processInstanceInfo.Tokens.Count == 0)
        {
            return null;
        }

        return processInstanceInfo.Tokens.Max(token => token.LastStateChangeTime);
    }
}
