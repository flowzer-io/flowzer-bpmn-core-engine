using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace FlowzerFrontend.Auth;

/// <summary>
/// Haengt das OIDC-Access-Token als Bearer an alle Aufrufe der Flowzer-Web-API.
/// Nur die konfigurierte API-Basisadresse gilt als autorisiertes Ziel; Tokens verlassen
/// die Oberflaeche zu keinem anderen Host.
/// </summary>
public sealed class FlowzerApiAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public FlowzerApiAuthorizationMessageHandler(
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigationManager,
        IWebAssemblyHostEnvironment hostEnvironment,
        FlowzerApiOptions apiOptions)
        : base(accessTokenProvider, navigationManager)
    {
        // ResolveBaseAddress liefert die Basis immer mit abschliessendem Schraegstrich; die
        // Bibliothek prueft per Praefix, sodass auch Unterpfade wie https://host/flowzer/ passen.
        // Alle FlowzerApi-Aufrufe sind relativ zu dieser Basis.
        var apiBaseAddress = apiOptions.ResolveBaseAddress(hostEnvironment.BaseAddress);
        ConfigureHandler(authorizedUrls: [apiBaseAddress.ToString()]);
    }
}
