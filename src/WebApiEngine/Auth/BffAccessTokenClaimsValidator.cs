using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WebApiEngine.Auth;

/// <summary>
/// Prueft den beim Code-Flow erhaltenen Access Token nochmals gegen die API-Audience und baut
/// daraus eine minimierte Cookie-Identitaet. Insbesondere Keycloak-Clientrollen stammen nicht
/// verlaesslich aus dem ID-Token und duerfen deshalb nicht ungeprueft uebernommen werden.
/// </summary>
public sealed class BffAccessTokenClaimsValidator(FlowzerAuthenticationOptions options)
{
    private static readonly HashSet<string> AllowedClaimTypes =
        new(StringComparer.Ordinal)
        {
            "iss", "sub", "oid", "preferred_username", "email", "upn", "unique_name", "name",
            "groups",
            ClaimTypes.NameIdentifier, ClaimTypes.Email, ClaimTypes.Name, ClaimTypes.GroupSid
        };

    private static readonly HashSet<string> AllowedIdTokenClaimTypes =
        new(StringComparer.Ordinal)
        {
            "sub", "preferred_username", "email", "upn", "unique_name", "name",
            ClaimTypes.NameIdentifier, ClaimTypes.Email, ClaimTypes.Name
        };

    public async Task<ClaimsPrincipal> CreateCookiePrincipalAsync(TokenValidatedContext context)
    {
        var accessToken = context.TokenEndpointResponse?.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new SecurityTokenValidationException("The OIDC token response did not contain an access token.");
        }

        if (context.Options.ConfigurationManager is null)
        {
            throw new SecurityTokenValidationException("OIDC metadata is unavailable for access-token validation.");
        }

        var configuration = await context.Options.ConfigurationManager.GetConfigurationAsync(context.HttpContext.RequestAborted);
        var principal = await ValidateAccessTokenAsync(
            accessToken,
            context.Principal ?? new ClaimsPrincipal(),
            configuration,
            context.HttpContext.RequestAborted);

        // Ohne erneute Providerpruefung duerfen die kopierten Rechte nie laenger gelten als
        // das validierte Access Token. Keine gleitende Verlaengerung mit eingefrorenen Rollen.
        var now = DateTimeOffset.UtcNow;
        var tokenExpiresAt = new DateTimeOffset(new JsonWebToken(accessToken).ValidTo);
        if (tokenExpiresAt <= now)
        {
            throw new SecurityTokenValidationException("The OIDC access token has expired.");
        }

        context.Properties ??= new Microsoft.AspNetCore.Authentication.AuthenticationProperties();
        context.Properties.IssuedUtc = now;
        context.Properties.ExpiresUtc = tokenExpiresAt < now.AddHours(8) ? tokenExpiresAt : now.AddHours(8);
        context.Properties.AllowRefresh = false;
        return principal;
    }

    /// <summary>
    /// Separater, deterministisch testbarer Sicherheitskern. Der Aufrufer muss die bereits ueber
    /// TLS bezogenen OIDC-Metadaten derselben Anmeldung uebergeben.
    /// </summary>
    public async Task<ClaimsPrincipal> ValidateAccessTokenAsync(
        string accessToken,
        ClaimsPrincipal idTokenPrincipal,
        Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateIssuer = true,
            ValidIssuer = configuration.Issuer,
            ValidateAudience = true,
            ValidAudience = options.JwtBearer.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        });
        if (!validation.IsValid || validation.ClaimsIdentity is null)
        {
            throw new SecurityTokenValidationException("The OIDC access token is invalid.", validation.Exception);
        }

        var accessPrincipal = new ClaimsPrincipal(validation.ClaimsIdentity);
        var idSubject = idTokenPrincipal.FindFirstValue("sub")
                        ?? idTokenPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        var accessSubject = accessPrincipal.FindFirstValue("sub")
                            ?? accessPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(idSubject)
            || string.IsNullOrWhiteSpace(accessSubject)
            || !string.Equals(idSubject, accessSubject, StringComparison.Ordinal))
        {
            throw new SecurityTokenValidationException("ID token and access token identify different subjects.");
        }

        var claims = accessPrincipal.Claims
            // ID-Token-Claims dienen nur der Anzeige und der bereits geprueften Subjektbindung.
            // Autorisierungsclaims (Rollen, Gruppen, oid, Issuer) stammen ausschliesslich aus
            // dem fuer die API-Audience validierten Access Token.
            .Concat(idTokenPrincipal.Claims.Where(claim => AllowedIdTokenClaimTypes.Contains(claim.Type)))
            .Where(claim => AllowedClaimTypes.Contains(claim.Type))
            .GroupBy(claim => (claim.Type, claim.Value, claim.ValueType), StringTupleComparer.Instance)
            .Select(group => group.First())
            .Select(claim => new Claim(claim.Type, claim.Value, claim.ValueType, claim.Issuer, claim.OriginalIssuer))
            .ToList();

        // Im Cookie landen nicht saemtliche Rollen aller Clients. Nur die fuer diesen Flowzer-
        // Betrieb konfigurierten Rollen werden aus dem validierten API-Token kanonisiert.
        foreach (var role in new[]
                 {
                     options.JwtBearer.RequiredRole,
                     options.JwtBearer.Roles.Modeler,
                     options.JwtBearer.Roles.Operator,
                     options.JwtBearer.Roles.Worker
                 }.Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal))
        {
            if (TokenRoles.HasRole(accessPrincipal, options.JwtBearer.Audience, role))
            {
                claims.Add(new Claim(TokenRoles.RolesClaim, role));
            }
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            FlowzerAuthenticationSchemes.Cookie,
            "name",
            TokenRoles.RolesClaim));
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Type, string Value, string ValueType)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals(
            (string Type, string Value, string ValueType) x,
            (string Type, string Value, string ValueType) y) =>
            string.Equals(x.Type, y.Type, StringComparison.Ordinal)
            && string.Equals(x.Value, y.Value, StringComparison.Ordinal)
            && string.Equals(x.ValueType, y.ValueType, StringComparison.Ordinal);

        public int GetHashCode((string Type, string Value, string ValueType) obj) => HashCode.Combine(obj.Type, obj.Value, obj.ValueType);
    }
}
