using System.Security.Claims;
using Microsoft.Extensions.Hosting;

namespace WebApiEngine.Auth;

/// <summary>
/// Liest den aktuellen Benutzerkontext bevorzugt aus Auth-Claims und erlaubt
/// im Development-Modus zusätzlich einen technischen Header. Fehlt beides,
/// wird ein stabiler Systembenutzer zurückgegeben, sodass die restliche API
/// keine Guid.Empty-/Magic-Value-Platzhalter mehr benötigt und in Produktion
/// kein frei setzbarer Impersonation-Header akzeptiert wird.
/// </summary>
public sealed class HttpContextCurrentUserContextAccessor(
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment hostEnvironment)
    : ICurrentUserContextAccessor
{
    public const string UserIdHeaderName = "X-Flowzer-UserId";
    public static readonly Guid FallbackUserId = Guid.Parse("D266F2B6-E96E-4D4A-9C20-C8E541394DF0");

    public CurrentUserContext GetCurrentUser()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var user = httpContext?.User;

        var claimBasedUser = TryResolveClaim(user, ClaimTypes.NameIdentifier, "claim:nameidentifier")
                             ?? TryResolveClaim(user, "sub", "claim:sub")
                             ?? TryResolveClaim(user, "oid", "claim:oid");
        if (claimBasedUser is not null)
        {
            return claimBasedUser;
        }

        var headerValue = httpContext?.Request.Headers[UserIdHeaderName].FirstOrDefault();
        if (hostEnvironment.IsDevelopment() && Guid.TryParse(headerValue, out var headerUserId))
        {
            return new CurrentUserContext(headerUserId, "header:x-flowzer-userid", false);
        }

        return new CurrentUserContext(FallbackUserId, "fallback:system-user", true);
    }

    private static CurrentUserContext? TryResolveClaim(
        ClaimsPrincipal? user,
        string claimType,
        string source)
    {
        var claimValue = user?.FindFirstValue(claimType);
        if (!Guid.TryParse(claimValue, out var userId))
        {
            return null;
        }

        return new CurrentUserContext(userId, source, false);
    }
}
