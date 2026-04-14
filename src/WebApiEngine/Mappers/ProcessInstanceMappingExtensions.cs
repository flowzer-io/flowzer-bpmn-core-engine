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

        var definitionMetadata = await definitionStorage.GetMetaDefinitionById(processInstanceInfo.metaDefinitionId);

        return new ProcessInstanceInfoDto
        {
            InstanceId = processInstanceInfo.InstanceId,
            DefinitionId = processInstanceInfo.DefinitionId,
            RelatedDefinitionId = processInstanceInfo.metaDefinitionId,
            RelatedDefinitionName = definitionMetadata.Name,
            MessageSubscriptionCount = processInstanceInfo.MessageSubscriptionCount,
            SignalSubscriptionCount = processInstanceInfo.SignalSubscriptionCount,
            UserTaskSubscriptionCount = processInstanceInfo.UserTaskSubscriptionCount,
            ServiceSubscriptionCount = processInstanceInfo.ServiceSubscriptionCount,
            State = (ProcessInstanceStateDto)processInstanceInfo.State,
            Tokens = processInstanceInfo.Tokens.Select(token => token.ToDto()).ToList()
        };
    }

    public static async Task<List<ProcessInstanceInfoDto>> ToDtosAsync(
        this IEnumerable<ProcessInstanceInfo> processInstances,
        IDefinitionStorage definitionStorage)
    {
        ArgumentNullException.ThrowIfNull(processInstances);
        ArgumentNullException.ThrowIfNull(definitionStorage);

        var mappedInstances = await Task.WhenAll(
            processInstances.Select(instance => instance.ToDtoAsync(definitionStorage)));

        return mappedInstances.ToList();
    }
}
