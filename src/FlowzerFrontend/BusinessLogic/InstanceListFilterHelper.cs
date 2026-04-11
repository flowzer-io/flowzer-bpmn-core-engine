using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Kapselt die UI-seitige Filterlogik für die Instanzliste, damit Routing,
/// Navigation und Tests dieselben Kategorien verwenden.
/// </summary>
public enum InstanceListFilter
{
    All,
    Active,
    Done,
    Error
}

public static class InstanceListFilterHelper
{
    public static InstanceListFilter ParseOrDefault(string? filter)
    {
        return filter?.Trim().ToLowerInvariant() switch
        {
            "active" => InstanceListFilter.Active,
            "done" => InstanceListFilter.Done,
            "error" => InstanceListFilter.Error,
            _ => InstanceListFilter.All
        };
    }

    public static string ToRouteSegment(InstanceListFilter filter)
    {
        return filter switch
        {
            InstanceListFilter.Active => "active",
            InstanceListFilter.Done => "done",
            InstanceListFilter.Error => "error",
            _ => "all"
        };
    }

    public static string ToDisplayLabel(InstanceListFilter filter)
    {
        return filter switch
        {
            InstanceListFilter.Active => "Active instances",
            InstanceListFilter.Done => "Completed instances",
            InstanceListFilter.Error => "Failed instances",
            _ => "All instances"
        };
    }

    public static IEnumerable<ProcessInstanceInfoDto> Apply(
        IEnumerable<ProcessInstanceInfoDto> instances,
        InstanceListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(instances);

        return filter switch
        {
            InstanceListFilter.Active => instances.Where(instance => instance.State is
                ProcessInstanceStateDto.Initialized or
                ProcessInstanceStateDto.Running or
                ProcessInstanceStateDto.Waiting or
                ProcessInstanceStateDto.Completing or
                ProcessInstanceStateDto.Terminating or
                ProcessInstanceStateDto.Compensating),
            InstanceListFilter.Done => instances.Where(instance => instance.State is
                ProcessInstanceStateDto.Completed or
                ProcessInstanceStateDto.Terminated or
                ProcessInstanceStateDto.Compensated),
            InstanceListFilter.Error => instances.Where(instance => instance.State is
                ProcessInstanceStateDto.Failing or
                ProcessInstanceStateDto.Failed),
            _ => instances
        };
    }
}
