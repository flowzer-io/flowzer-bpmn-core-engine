using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FlowzerFrontend.Auth;

/// <summary>
/// Ersatz fuer den OIDC-Anmeldezustand, solange kein Identity Provider konfiguriert ist.
/// Die Oberflaeche gilt dann als angemeldet ("technischer Benutzer"), damit dieselben
/// [Authorize]-Regeln greifen wie mit OIDC. Die Web-API identifiziert diesen Benutzer im
/// Development-Modus ueber den Header X-Flowzer-UserId; ausserhalb davon lehnt sie
/// benutzerbezogene Aufrufe ab.
/// </summary>
public sealed class DevelopmentAuthenticationStateProvider(FlowzerApiOptions apiOptions) : AuthenticationStateProvider
{
    public const string DisplayName = "Technischer Benutzer";

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, DisplayName) };
        if (!string.IsNullOrWhiteSpace(apiOptions.DevelopmentUserId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, apiOptions.DevelopmentUserId));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: "development");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}
