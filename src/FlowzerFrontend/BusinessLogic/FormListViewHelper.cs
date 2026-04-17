using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Kapselt Such- und Sortierlogik der Formularliste, damit Seite und Tests
/// dieselben Regeln für Filterung und Standardreihenfolge verwenden.
/// </summary>
public static class FormListViewHelper
{
    public static IEnumerable<FormMetaDataDto> ApplyQuery(
        IEnumerable<FormMetaDataDto> forms,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(forms);

        var filteredForms = forms;
        var normalizedSearch = searchText?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            filteredForms = filteredForms.Where(form => MatchesSearch(form, normalizedSearch));
        }

        return filteredForms
            .OrderBy(form => form.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(form => form.FormId);
    }

    private static bool MatchesSearch(FormMetaDataDto form, string normalizedSearch)
    {
        return Contains(form.Name, normalizedSearch)
               || form.FormId.ToString().Contains(normalizedSearch, StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.InvariantCultureIgnoreCase) == true;
    }
}
