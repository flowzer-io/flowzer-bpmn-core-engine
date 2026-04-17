using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Kapselt die kombinierte Filter-, Such- und Sortierlogik der Instanzliste,
/// damit die UI denselben Blick auf Laufzeitdaten überall konsistent nutzt.
/// </summary>
public static class InstanceListViewHelper
{
    public static IEnumerable<ProcessInstanceInfoDto> ApplyQuery(
        IEnumerable<ProcessInstanceInfoDto> instances,
        InstanceListFilter filter,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(instances);

        var filteredInstances = InstanceListFilterHelper.Apply(instances, filter);
        var normalizedSearch = searchText?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            filteredInstances = filteredInstances.Where(instance => MatchesSearch(instance, normalizedSearch));
        }

        return filteredInstances
            .OrderBy(instance => instance.RelatedDefinitionName, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(instance => instance.InstanceId);
    }

    private static bool MatchesSearch(ProcessInstanceInfoDto instance, string normalizedSearch)
    {
        return Contains(instance.RelatedDefinitionName, normalizedSearch)
               || Contains(instance.RelatedDefinitionId, normalizedSearch)
               || instance.InstanceId.ToString().Contains(normalizedSearch, StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.InvariantCultureIgnoreCase) == true;
    }
}
