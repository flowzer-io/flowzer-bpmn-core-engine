namespace FlowzerFrontend.Auth;

/// <summary>
/// OIDC-Konfiguration des Frontends (Abschnitt <c>Oidc</c> in <c>wwwroot/appsettings*.json</c>).
/// Sind <see cref="Authority"/> und <see cref="ClientId"/> gesetzt, meldet sich die Oberflaeche
/// beim Identity Provider an und sendet das Access-Token als Bearer an die Web-API. Ohne
/// Konfiguration laeuft das Frontend wie bisher mit dem technischen Development-Benutzer.
/// </summary>
public sealed class FlowzerOidcOptions
{
    public const string SectionName = "Oidc";
    public const string LoginPath = "authentication/login";
    public const string LogoutPath = "authentication/logout";

    /// <summary>OIDC-Issuer, z. B. <c>https://login.microsoftonline.com/{tenant}/v2.0</c> oder ein Keycloak-Realm.</summary>
    public string? Authority { get; set; }

    /// <summary>Client-Id der Oberflaeche (SPA / Public Client mit PKCE).</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Angeforderte Scopes. Zusaetzlich zu <c>openid profile</c> gehoert hier der Scope der API hinein,
    /// damit das Access-Token die Audience der API traegt (Entra ID: <c>api://{api-client-id}/access_as_user</c>).
    /// </summary>
    public List<string> Scopes { get; set; } = [];

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>
    /// Liefert die effektiven Scopes: <c>openid</c> und <c>profile</c> sind immer dabei, Duplikate
    /// und Leerwerte werden entfernt.
    /// </summary>
    public IReadOnlyList<string> ResolveScopes()
    {
        var scopes = new List<string> { "openid", "profile" };
        foreach (var scope in Scopes)
        {
            var trimmed = scope?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !scopes.Contains(trimmed, StringComparer.Ordinal))
            {
                scopes.Add(trimmed);
            }
        }

        return scopes;
    }

    /// <summary>
    /// Eine halbe Konfiguration ist ein Betriebsfehler: entweder beides oder nichts.
    /// </summary>
    public void Validate()
    {
        var hasAuthority = !string.IsNullOrWhiteSpace(Authority);
        var hasClientId = !string.IsNullOrWhiteSpace(ClientId);
        if (hasAuthority != hasClientId)
        {
            throw new InvalidOperationException(
                "Oidc:Authority and Oidc:ClientId must both be set to enable OIDC login, or both be empty.");
        }
    }
}
