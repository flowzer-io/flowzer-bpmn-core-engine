using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Kleine Browser-Fassade fuer serverseitige OIDC-Anmeldung, Session und CSRF. Externe Clients
/// verwenden weiterhin den Bearer-Vertrag der Fachcontroller.
/// </summary>
[ApiController]
[Route("bff")]
public sealed class BffController(
    FlowzerAuthenticationOptions options,
    ICurrentUserContextAccessor currentUserContextAccessor,
    IAuthorizationService authorizationService,
    IAntiforgery antiforgery,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    ILogger<BffController> logger) : ControllerBase
{
    // Obergrenze fuer das Laden der OIDC-Metadaten beim Provider-Logout. Die Metadaten sind
    // nach der Anmeldung normalerweise zwischengespeichert; die Abmeldung soll nie haengen.
    private static readonly TimeSpan ProviderMetadataTimeout = TimeSpan.FromSeconds(10);

    [AllowAnonymous]
    [HttpGet("login")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Login([FromQuery] string? returnTo = "/")
    {
        if (!options.IsBffEnabled)
        {
            return NotFound();
        }

        if (!IsSafeLocalReturnTarget(returnTo))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid return target.",
                detail: "The returnTo value must be a local absolute path.",
                type: "https://flowzer.io/problems/invalid-return-target");
        }

        return Challenge(
            new AuthenticationProperties { RedirectUri = returnTo },
            FlowzerAuthenticationSchemes.OpenIdConnect);
    }

    // Sitzungsverwaltung braucht eine Anmeldung, aber keine fachliche Freischaltung.
    [Authorize(Policy = FlowzerPolicies.Session)]
    [HttpGet("session")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<BffSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BffSessionDto>> GetSession()
    {
        if (!options.IsBffEnabled)
        {
            return NotFound();
        }

        var currentUser = currentUserContextAccessor.GetCurrentUser();
        if (currentUser.IsFallback)
        {
            return Unauthorized();
        }

        var capabilities = new List<string>();
        foreach (var (capabilityName, policy) in new[]
                 {
                     ("access", FlowzerPolicies.Access),
                     ("modeler", FlowzerPolicies.Modeler),
                     ("operator", FlowzerPolicies.Operator),
                     ("worker", FlowzerPolicies.Worker),
                     ("aiConnectionUse", FlowzerPolicies.AiConnectionUse),
                     ("aiConnectionManage", FlowzerPolicies.AiConnectionManage)
                 })
        {
            if ((await authorizationService.AuthorizeAsync(User, policy)).Succeeded)
            {
                capabilities.Add(capabilityName);
            }
        }

        var name = User.FindFirstValue("name")
                   ?? User.FindFirstValue("preferred_username")
                   ?? User.FindFirstValue("email")
                   ?? currentUser.UserId.ToString();
        var email = User.FindFirstValue("email") ?? User.FindFirstValue(ClaimTypes.Email);
        return Ok(new BffSessionDto(currentUser.UserId.ToString(), name, email, capabilities));
    }

    // Sitzungsverwaltung braucht eine Anmeldung, aber keine fachliche Freischaltung.
    [Authorize(Policy = FlowzerPolicies.Session)]
    [HttpGet("csrf")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<BffCsrfDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<BffCsrfDto> GetCsrfToken()
    {
        if (!options.IsBffEnabled)
        {
            return NotFound();
        }

        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            throw new InvalidOperationException("ASP.NET Core did not issue an antiforgery request token.");
        }

        return Ok(new BffCsrfDto(tokens.RequestToken, tokens.HeaderName ?? "X-Flowzer-CSRF"));
    }

    // Sitzungsverwaltung braucht eine Anmeldung, aber keine fachliche Freischaltung.
    // 204: nur die Flowzer-Sitzung ist beendet. 200: zusaetzlich liefert die Antwort die
    // Abmeldeadresse des Identity Providers (nur mit Authentication:Bff:ProviderLogout).
    [Authorize(Policy = FlowzerPolicies.Session)]
    [HttpPost("logout")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<BffLogoutResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Logout()
    {
        if (!options.IsBffEnabled)
        {
            return NotFound();
        }

        // Vor dem Beenden lesen: Danach ist das serverseitige Ticket samt ID-Token entfernt.
        var idToken = options.Bff.ProviderLogout
            ? await HttpContext.GetTokenAsync(FlowzerAuthenticationSchemes.Cookie, BffSessionStore.IdToken)
            : null;

        await HttpContext.SignOutAsync(FlowzerAuthenticationSchemes.Cookie);

        var redirectTo = options.Bff.ProviderLogout ? await BuildProviderLogoutUrlAsync(idToken) : null;
        return redirectTo is null ? NoContent() : Ok(new BffLogoutResponseDto(redirectTo));
    }

    /// <summary>
    /// Baut die Adresse fuer den RP-initiated Logout (OpenID Connect RP-Initiated Logout 1.0).
    /// Best Effort: Die lokale Sitzung ist an dieser Stelle bereits beendet. Fehlen Metadaten
    /// oder ein <c>end_session_endpoint</c>, bleibt es bei der lokalen Abmeldung (204).
    /// </summary>
    private async Task<string?> BuildProviderLogoutUrlAsync(string? idToken)
    {
        var oidc = oidcOptions.Get(FlowzerAuthenticationSchemes.OpenIdConnect);
        OpenIdConnectConfiguration configuration;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(ProviderMetadataTimeout);
        try
        {
            if (oidc.ConfigurationManager is null)
            {
                throw new InvalidOperationException("OIDC metadata is unavailable.");
            }

            configuration = await oidc.ConfigurationManager.GetConfigurationAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException
                                              or OperationCanceledException or IOException)
        {
            // Nur der Ausnahmetyp: Meldungen und Antworten der Metadatenabfrage bleiben aus dem Log.
            logger.LogWarning(
                "Provider logout skipped because the OIDC metadata could not be loaded ({ExceptionType}); the local session has ended.",
                exception.GetType().Name);
            return null;
        }

        var endSessionEndpoint = configuration.EndSessionEndpoint;
        if (string.IsNullOrWhiteSpace(endSessionEndpoint))
        {
            return null;
        }

        if (!Uri.TryCreate(endSessionEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps
                && (oidc.RequireHttpsMetadata || endpoint.Scheme != Uri.UriSchemeHttp)))
        {
            logger.LogWarning("Provider logout skipped because the advertised end_session_endpoint is not a usable HTTPS address.");
            return null;
        }

        // Scheme und Host stimmen hinter dem TLS-Proxy ueber die ausgewerteten Forwarded-Header.
        // Der Pfad ist in Validate() auf einen lokalen absoluten Pfad beschraenkt.
        var postLogoutRedirectUri = $"{Request.Scheme}://{Request.Host.ToUriComponent()}{options.Bff.PostLogoutPath}";
        var parameters = new List<KeyValuePair<string, string?>>();
        // Ohne ID-Token (etwa bei einer Sitzung von vor dem Update) genuegen client_id und
        // post_logout_redirect_uri. Der Provider kann den Logout dann nicht der Sitzung zuordnen
        // und fragt nach (Keycloak: Bestaetigungsseite); das ist fuer diesen Uebergang akzeptabel.
        if (!string.IsNullOrWhiteSpace(idToken))
        {
            parameters.Add(new("id_token_hint", idToken));
        }

        parameters.Add(new("post_logout_redirect_uri", postLogoutRedirectUri));
        parameters.Add(new("client_id", options.Bff.ClientId));
        return QueryHelpers.AddQueryString(endSessionEndpoint, parameters);
    }

    private bool IsSafeLocalReturnTarget(string? returnTo) =>
        FlowzerAuthenticationOptions.IsLocalAbsolutePath(returnTo) && Url.IsLocalUrl(returnTo);
}
