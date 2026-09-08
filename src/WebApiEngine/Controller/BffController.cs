using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    IAntiforgery antiforgery) : ControllerBase
{
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
                     ("worker", FlowzerPolicies.Worker)
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
    [Authorize(Policy = FlowzerPolicies.Session)]
    [HttpPost("logout")]
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

        await HttpContext.SignOutAsync(FlowzerAuthenticationSchemes.Cookie);
        return NoContent();
    }

    private bool IsSafeLocalReturnTarget(string? returnTo) =>
        !string.IsNullOrWhiteSpace(returnTo)
        && returnTo.StartsWith("/", StringComparison.Ordinal)
        && !returnTo.StartsWith("//", StringComparison.Ordinal)
        && !returnTo.Contains('\\')
        && !returnTo.Contains('\r')
        && !returnTo.Contains('\n')
        && Url.IsLocalUrl(returnTo);
}
