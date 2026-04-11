using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Liest häufig benötigte Anzeigeinformationen defensiv aus Token-Daten aus.
/// So bleiben UI-Komponenten auch dann stabil, wenn optionale Expando-Felder fehlen.
/// </summary>
public static class TokenDisplayHelper
{
    public static string? GetImplementation(TokenDto token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token.CurrentFlowElement.GetValue<string>("Implementation", null);
    }

    public static string? GetFlowElementId(TokenDto token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token.CurrentFlowElement.GetValue<string>("Id", token.CurrentFlowNodeId);
    }

    public static string GetDisplayName(TokenDto token, string fallback)
    {
        ArgumentNullException.ThrowIfNull(token);

        return token.CurrentFlowElement.GetValue<string>("Name", null)
               ?? token.CurrentFlowElement.GetValue<string>("Id", fallback)
               ?? fallback;
    }
}
