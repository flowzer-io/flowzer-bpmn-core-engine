namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Parst die Formularroute defensiv, damit ungültige URLs nicht die komplette
/// Seite mit einer Guid.Parse-Ausnahme abbrechen lassen.
/// </summary>
public sealed record FormRouteState(
    bool IsCreate,
    Guid? FormId,
    string? ErrorMessage);

public static class FormRouteHelper
{
    public static FormRouteState Parse(string? formId)
    {
        if (string.IsNullOrWhiteSpace(formId))
        {
            return new FormRouteState(false, null, "No form id was provided in the route.");
        }

        if (string.Equals(formId, "create", StringComparison.OrdinalIgnoreCase))
        {
            return new FormRouteState(true, null, null);
        }

        return Guid.TryParse(formId, out var parsedFormId)
            ? new FormRouteState(false, parsedFormId, null)
            : new FormRouteState(false, null, $"The form route value '{formId}' is not a valid form id.");
    }
}
