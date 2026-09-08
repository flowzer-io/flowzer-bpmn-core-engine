using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using WebApiEngine.Auth;

namespace WebApiEngine.Tests;

/// <summary>Prueft den sicherheitskritischen Claim-Transfer vom OIDC Access Token ins Session-Cookie.</summary>
public sealed class BffAccessTokenClaimsValidatorTest
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-bff-validator-signing-key-which-is-long-enough"));

    // Testzweck: Nur serverseitig validierte, ausdruecklich erlaubte Identitaets-, Gruppen- und
    // Rollenclaims duerfen ins Cookie gelangen; technische oder fremde Claims bleiben draussen.
    [Test]
    public async Task ValidateAccessToken_ShouldReturnOnlyAllowlistedClaims()
    {
        var subject = Guid.NewGuid().ToString();
        var validator = CreateValidator();
        var idPrincipal = Principal(
            new Claim("sub", subject),
            new Claim("name", "Ada Lovelace"),
            new Claim("roles", "admin-from-id-token"),
            new Claim("groups", "/forged-id-token-group"));
        var token = CreateToken(Audience,
            new Claim("sub", subject),
            new Claim("email", "ada@example.test"),
            new Claim("groups", "/engineering"),
            new Claim("resource_access", """{"flowzer-api":{"roles":["access"]}}""", JsonClaimValueTypes.Json),
            new Claim("untrusted_custom", "must-not-leak"));

        var result = await validator.ValidateAccessTokenAsync(token, idPrincipal, Configuration());

        result.Identity!.IsAuthenticated.Should().BeTrue();
        result.FindFirstValue("sub").Should().Be(subject);
        result.FindFirstValue("email").Should().Be("ada@example.test");
        result.FindFirstValue("groups").Should().Be("/engineering");
        TokenRoles.HasRole(result, Audience, "access").Should().BeTrue();
        result.FindFirst(TokenRoles.ResourceAccessClaim).Should().BeNull();
        result.FindAll("roles").Should().NotContain(claim => claim.Value == "admin-from-id-token");
        result.FindAll("groups").Should().NotContain(claim => claim.Value == "/forged-id-token-group");
        result.FindFirst("untrusted_custom").Should().BeNull();
        result.FindFirst("aud").Should().BeNull();
        result.FindFirst("exp").Should().BeNull();
    }

    // Testzweck: Ein Access Token fuer eine andere API darf trotz gueltiger Signatur nicht als
    // Browser-Session dienen.
    [Test]
    public async Task ValidateAccessToken_ShouldRejectWrongAudience()
    {
        var subject = Guid.NewGuid().ToString();
        var validator = CreateValidator();

        var action = () => validator.ValidateAccessTokenAsync(
            CreateToken("other-api", new Claim("sub", subject)),
            Principal(new Claim("sub", subject)),
            Configuration());

        await action.Should().ThrowAsync<SecurityTokenValidationException>();
    }

    // Testzweck: ID- und Access-Token muessen dieselbe Person benennen; andernfalls koennte eine
    // vermischte Providerantwort Rollen eines anderen Kontos in die Session uebernehmen.
    [Test]
    public async Task ValidateAccessToken_ShouldRejectSubjectMismatch()
    {
        var validator = CreateValidator();

        var action = () => validator.ValidateAccessTokenAsync(
            CreateToken(Audience, new Claim("sub", Guid.NewGuid().ToString())),
            Principal(new Claim("sub", Guid.NewGuid().ToString())),
            Configuration());

        await action.Should().ThrowAsync<SecurityTokenValidationException>()
            .WithMessage("*different subjects*");
    }

    // Testzweck: Eine Cookie-Sitzung darf die geprueften Rechte weder ueber das Access-Token-
    // Ende hinaus tragen noch durch aktive Nutzung unbemerkt verlaengern.
    [Test]
    public async Task CreateCookiePrincipal_ShouldBindSessionToAccessTokenLifetime()
    {
        var subject = Guid.NewGuid().ToString();
        var token = CreateToken(Audience, new Claim("sub", subject));
        var context = CreateContext(token, subject);

        await CreateValidator().CreateCookiePrincipalAsync(context);

        context.Properties!.ExpiresUtc.Should().Be(new DateTimeOffset(new JsonWebToken(token).ValidTo));
        context.Properties.AllowRefresh.Should().BeFalse();
        context.Properties.GetTokens().Should().BeEmpty();
    }

    // Testzweck: Auch ein Provider mit sehr langer Access-Token-Laufzeit darf keine unbeschraenkte
    // Flowzer-Sitzung erzeugen; spaetestens nach acht Stunden ist eine erneute Anmeldung noetig.
    [Test]
    public async Task CreateCookiePrincipal_ShouldCapLongProviderLifetime()
    {
        var subject = Guid.NewGuid().ToString();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddDays(2),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([new Claim("sub", subject)])
        });
        var context = CreateContext(token, subject);
        // AuthenticationProperties speichert Zeitwerte mit Sekundengenauigkeit.
        var before = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await CreateValidator().CreateCookiePrincipalAsync(context);

        context.Properties!.ExpiresUtc.Should().BeOnOrAfter(before.AddHours(8))
            .And.BeOnOrBefore(DateTimeOffset.UtcNow.AddHours(8));
        context.Properties.AllowRefresh.Should().BeFalse();
    }

    // Testzweck: Ein signierter, aber mit einem fremden Schluessel ausgestellter Access Token
    // darf keine Session erzeugen, selbst wenn Issuer, Subject und Audience passen.
    [Test]
    public async Task ValidateAccessToken_ShouldRejectUntrustedSignature()
    {
        var subject = Guid.NewGuid().ToString();
        var configuration = Configuration();
        configuration.SigningKeys.Clear();
        configuration.SigningKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            "another-untrusted-signing-key-which-is-long-enough")));

        var action = () => CreateValidator().ValidateAccessTokenAsync(
            CreateToken(Audience, new Claim("sub", subject)), Principal(new Claim("sub", subject)), configuration);

        await action.Should().ThrowAsync<SecurityTokenValidationException>();
    }

    private static TokenValidatedContext CreateContext(string accessToken, string subject) => new(
        new DefaultHttpContext(),
        new AuthenticationScheme(FlowzerAuthenticationSchemes.OpenIdConnect, null, typeof(OpenIdConnectHandler)),
        new OpenIdConnectOptions
        {
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(Configuration())
        },
        Principal(new Claim("sub", subject)),
        new AuthenticationProperties())
    {
        TokenEndpointResponse = new OpenIdConnectMessage { AccessToken = accessToken }
    };

    private static BffAccessTokenClaimsValidator CreateValidator() => new(new FlowzerAuthenticationOptions
    {
        Scheme = FlowzerAuthenticationOptions.SchemeBff,
        JwtBearer = new FlowzerAuthenticationOptions.JwtBearerSettings
        {
            Authority = Issuer,
            Audience = Audience,
            RequiredRole = "access"
        },
        Bff = new FlowzerAuthenticationOptions.BffSettings
        {
            ClientId = "flowzer-console",
            ClientSecret = "not-used-in-this-unit-test",
            DataProtectionKeysPath = Path.GetTempPath()
        }
    });

    private static OpenIdConnectConfiguration Configuration() => new()
    {
        Issuer = Issuer,
        SigningKeys = { SigningKey }
    };

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static string CreateToken(string audience, params Claim[] claims) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims)
        });
}
