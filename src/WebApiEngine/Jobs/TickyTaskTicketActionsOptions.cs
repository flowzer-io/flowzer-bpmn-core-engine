using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Jobs;

/// <summary>
/// Installationsbindung für die vier TT-Ticket-Service-Tasks. Ohne ausdrücklich
/// konfigurierten TT-API-Client bleibt ihr Initiatornachweis geschlossen (503).
/// Kein Request, Prozessfeld oder Worker darf Client oder Pflichtrolle wählen.
/// </summary>
public sealed class TickyTaskTicketActionsOptions
{
    public const string SectionName = "TickyTaskTicketActions";

    /// <summary>TT verlangt dieselbe feste Client-Zugangsrolle am eigenen API-Eingang.</summary>
    public const string RequiredRole = "access";

    /// <summary>
    /// Exakter Keycloak-Client-ID-Wert der TT-API (<c>Oidc:Audience</c> in TT),
    /// nicht der Flowzer-API-Client, Browserclient oder technische Workerclient.
    /// Leer ist der sichere Default für nicht eingerichtete Integrationen.
    /// </summary>
    public string ApiClientId { get; set; } = string.Empty;

    /// <summary>Kein Trimmen, URI-Fallback oder Ersatz durch den Flowzer-Client.</summary>
    internal static bool IsValidApiClientId(string? apiClientId, string flowzerApiClientId) => apiClientId is { Length: > 0 and <= 256 }
        && KeycloakDirectoryOptions.IsSafeProviderId(apiClientId)
        && !string.Equals(apiClientId, flowzerApiClientId, StringComparison.Ordinal);
}
