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
            return claimBasedUser with { Names = CollectNames(user), Groups = CollectGroups(user) };
        }

        // Der technische Header ist nur ein Ersatz fuer fehlende Authentifizierung. Ist der
        // Request bereits authentifiziert (JWT ohne verwertbaren GUID-Claim), darf der Header
        // die Identitaet nicht ueberschreiben.
        var isAuthenticated = user?.Identity?.IsAuthenticated ?? false;
        var headerValue = httpContext?.Request.Headers[UserIdHeaderName].FirstOrDefault();
        if (hostEnvironment.IsDevelopment() && !isAuthenticated && Guid.TryParse(headerValue, out var headerUserId))
        {
            return new CurrentUserContext(headerUserId, "header:x-flowzer-userid", false);
        }

        return new CurrentUserContext(FallbackUserId, "fallback:system-user", true);
    }

    /// <summary>
    /// Sammelt alles, womit ein Modell die Person benennen koennte. Die technische Id gehoert
    /// dazu, weil manche Modelle sie direkt eintragen.
    /// </summary>
    private static IReadOnlyCollection<string> CollectNames(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return [];
        }

        string[] claimTypes =
        [
            "preferred_username", "email", "upn", "unique_name", "name",
            "sub", "oid", ClaimTypes.NameIdentifier, ClaimTypes.Email, ClaimTypes.Name
        ];

        return claimTypes
            .SelectMany(user.FindAll)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyCollection<string> CollectGroups(ClaimsPrincipal? user) =>
        user is null
            ? []
            : user.FindAll("groups")
                .Concat(user.FindAll(ClaimTypes.GroupSid))
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

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
