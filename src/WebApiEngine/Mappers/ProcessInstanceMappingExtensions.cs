using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>
/// Bündelt das Mapping von gespeicherten Prozessinstanzen in API-DTOs.
/// </summary>
public static class ProcessInstanceMappingExtensions
{
    public static async Task<ProcessInstanceInfoDto> ToDtoAsync(
        this ProcessInstanceInfo processInstanceInfo,
        IDefinitionStorage definitionStorage)
    {
        ArgumentNullException.ThrowIfNull(processInstanceInfo);
        ArgumentNullException.ThrowIfNull(definitionStorage);

        var metaNamesById = await GetMetaNamesByIdAsync(definitionStorage);
        return processInstanceInfo.ToDto(metaNamesById);
    }

    public static async Task<List<ProcessInstanceInfoDto>> ToDtosAsync(
        this IEnumerable<ProcessInstanceInfo> processInstances,
        IDefinitionStorage definitionStorage)
    {
        ArgumentNullException.ThrowIfNull(processInstances);
        ArgumentNullException.ThrowIfNull(definitionStorage);

        var metaNamesById = await GetMetaNamesByIdAsync(definitionStorage);
        return processInstances
            .Select(instance => instance.ToDto(metaNamesById))
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
        IReadOnlyDictionary<string, string> metaNamesById)
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
            MessageSubscriptionCount = processInstanceInfo.MessageSubscriptionCount,
            SignalSubscriptionCount = processInstanceInfo.SignalSubscriptionCount,
            UserTaskSubscriptionCount = processInstanceInfo.UserTaskSubscriptionCount,
            ServiceSubscriptionCount = processInstanceInfo.ServiceSubscriptionCount,
            State = (ProcessInstanceStateDto)processInstanceInfo.State,
            Tokens = processInstanceInfo.Tokens.Select(token => token.ToDto()).ToList()
        };
    }
}
