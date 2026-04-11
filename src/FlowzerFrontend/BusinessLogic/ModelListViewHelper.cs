using Microsoft.FluentUI.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Bündelt Such- und Sortierlogik der Modellliste, damit die Seite selbst nur
/// noch Lade- und Navigationszustände verwalten muss.
/// </summary>
public static class ModelListViewHelper
{
    public static IEnumerable<ExtendedBpmnMetaDefinitionDto> ApplyQuery(
        IEnumerable<ExtendedBpmnMetaDefinitionDto> models,
        string? searchText,
        SortDirection sortDirection)
    {
        ArgumentNullException.ThrowIfNull(models);

        var filteredModels = models;
        var normalizedSearch = searchText?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            filteredModels = filteredModels.Where(model => MatchesSearch(model, normalizedSearch));
        }

        return sortDirection == SortDirection.Descending
            ? filteredModels
                .OrderByDescending(model => model.Name, StringComparer.InvariantCultureIgnoreCase)
                .ThenByDescending(model => model.DefinitionId, StringComparer.InvariantCultureIgnoreCase)
            : filteredModels
                .OrderBy(model => model.Name, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(model => model.DefinitionId, StringComparer.InvariantCultureIgnoreCase);
    }

    private static bool MatchesSearch(ExtendedBpmnMetaDefinitionDto model, string normalizedSearch)
    {
        return Contains(model.Name, normalizedSearch)
               || Contains(model.DefinitionId, normalizedSearch)
               || Contains(model.Description, normalizedSearch);
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.InvariantCultureIgnoreCase) == true;
    }
}
