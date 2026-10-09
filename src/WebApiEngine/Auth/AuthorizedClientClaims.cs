using System.Security.Claims;

namespace WebApiEngine.Auth;

/// <summary>
/// Gemeinsame reine Claimgrenze für Bearer und BFF: Aus einem bereits authentisierten
/// Principal genau einen begrenzten OIDC-azp lesen, vor jeglicher Claim-Deduplizierung.
/// Das ist ein Auditfakt, niemals ein Besitz-/Autorisierungsrecht.
/// </summary>
internal static class AuthorizedClientClaims
{
    internal static string? Read(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        var clients = user.FindAll("azp").Take(2).ToArray();
        if (clients.Length == 0) return null; // Altprovider/Dev ohne OIDC-azp bleiben kompatibel.
        if (clients.Length != 1 || clients[0].Value.Length is < 1 or > 256
            || clients[0].Value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
            throw new UnauthorizedAccessException("An unambiguous authenticated client is required.");
        return clients[0].Value;
    }
}
