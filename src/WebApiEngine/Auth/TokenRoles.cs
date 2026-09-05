using System.Security.Claims;
using System.Text.Json;

namespace WebApiEngine.Auth;

/// <summary>
/// Liest Rollen aus einem OIDC-Access-Token ohne Inbound-Claim-Mapping: Keycloak-Clientrollen
/// (<c>resource_access.&lt;client&gt;.roles</c>, als JSON-Claim) und App-Rollen (<c>roles</c>, ein
/// Claim je Rolle, wie Entra ID sie liefert).
/// </summary>
public static class TokenRoles
{
    public const string ResourceAccessClaim = "resource_access";
    public const string RolesClaim = "roles";

    public static bool HasRole(ClaimsPrincipal user, string audience, string role)
    {
        if (user.FindAll(RolesClaim).Any(claim => string.Equals(claim.Value, role, StringComparison.Ordinal)))
        {
            return true;
        }

        var resourceAccess = user.FindFirst(ResourceAccessClaim)?.Value;
        if (string.IsNullOrWhiteSpace(resourceAccess))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(resourceAccess);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(audience, out var client)
                   && client.ValueKind == JsonValueKind.Object
                   && client.TryGetProperty("roles", out var roles)
                   && roles.ValueKind == JsonValueKind.Array
                   && roles.EnumerateArray().Any(entry => entry.ValueKind == JsonValueKind.String
                                                          && string.Equals(entry.GetString(), role, StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            // Ein unlesbarer Claim ist kein Nachweis der Rolle.
            return false;
        }
    }
}
