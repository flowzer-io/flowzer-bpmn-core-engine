using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Liest häufig benötigte Anzeigeinformationen defensiv aus Token-Daten aus.
/// So bleiben UI-Komponenten auch dann stabil, wenn optionale Expando-Felder fehlen.
/// </summary>
public static class TokenDisplayHelper
{
    private static string? GetOptionalString(TokenDto token, string key, string? fallback)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.CurrentFlowElement is not IDictionary<string, object?> values)
        {
            return fallback;
        }

        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value as string ?? fallback;
    }

    public static string? GetImplementation(TokenDto token)
    {
        return GetOptionalString(token, "Implementation", null);
    }

    public static string? GetFlowElementId(TokenDto token)
    {
        return GetOptionalString(token, "Id", token.CurrentFlowNodeId);
    }

    public static string GetDisplayName(TokenDto token, string fallback)
    {
        ArgumentNullException.ThrowIfNull(token);

        return GetOptionalString(token, "Name", null)
               ?? GetOptionalString(token, "Id", fallback)
               ?? fallback;
    }
}
