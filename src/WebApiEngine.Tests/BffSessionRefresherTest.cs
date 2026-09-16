using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using WebApiEngine.Auth;

namespace WebApiEngine.Tests;

public sealed class BffSessionRefresherTest
{
    private const string Issuer = "https://issuer.test";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("test-only-signing-key-with-at-least-thirty-two-bytes"));

    // Testzweck: Refresh ersetzt statt konserviert Rechte, rotiert ausschließlich das
    // serverseitige Refresh-Token und ändert die absolute Sitzungsfrist nicht.
    [Test]
    public async Task Refresh_ShouldReplaceRolesAndRotateToken()
    {
        var ticket = Ticket();
        var expiration = ticket.Properties.ExpiresUtc;
        using var client = Client(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            access_token = Token("api", "person"), refresh_token = "rotated-test-token"
        }));
        var result = await Refresher(client).RefreshAsync(ticket);
        result.Should().NotBeNull();
        result!.Principal.IsInRole("operator").Should().BeFalse();
        result.Principal.FindFirstValue("sub").Should().Be("person");
        result.Properties.ExpiresUtc.Should().Be(expiration);
        result.Properties.GetTokens().Should().ContainSingle().Which.Name.Should().Be("refresh_token");
        result.Properties.GetTokenValue("refresh_token").Should().Be("rotated-test-token");
    }

    // Testzweck: Ein widerrufener Grant und Tokens für eine andere API/Person dürfen die
    // Sitzung niemals verlängern; Fehler des Providers bleiben dagegen wiederholbar.
    [TestCase("revoked")]
    [TestCase("audience")]
    [TestCase("subject")]
    public async Task Refresh_ShouldRejectInvalidGrantOrIdentity(string scenario)
    {
        using var client = Client(scenario == "revoked" ? HttpStatusCode.BadRequest : HttpStatusCode.OK,
            scenario == "revoked" ? """{"error":"invalid_grant"}""" : JsonSerializer.Serialize(new
            {
                access_token = Token(scenario == "audience" ? "other-api" : "api", scenario == "subject" ? "other-person" : "person")
            }));
        (await Refresher(client).RefreshAsync(Ticket())).Should().BeNull();
    }

    // Testzweck: Temporäre Providerfehler und kaputte Antwortformen ergeben einen neutralen
    // 503-Pfad statt Logout oder Ausgabe vertraulicher Providerantworten.
    [TestCase(503, "unavailable")]
    [TestCase(200, "{}")]
    [TestCase(200, "{\"access_token\":42}")]
    [TestCase(200, "[]")]
    public async Task Refresh_ShouldReportTransientFailure(int status, string response)
    {
        using var client = Client((HttpStatusCode)status, response);
        Func<Task> refresh = () => Refresher(client).RefreshAsync(Ticket());
        await refresh.Should().ThrowAsync<BffSessionUnavailableException>();
    }

    private static AuthenticationTicket Ticket()
    {
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) };
        properties.StoreTokens([new AuthenticationToken { Name = "refresh_token", Value = "initial-test-token" }]);
        return new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "person"), new Claim(ClaimTypes.Role, "operator")], "test")), properties, FlowzerAuthenticationSchemes.Cookie);
    }

    private static string Token(string audience, string subject) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer, Audience = audience, Expires = DateTime.UtcNow.AddMinutes(5),
        Subject = new ClaimsIdentity([new Claim("sub", subject)]),
        SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256)
    });

    private static BffSessionRefresher Refresher(HttpClient client)
    {
        var options = new FlowzerAuthenticationOptions
        {
            JwtBearer = new() { Authority = Issuer, Audience = "api", RequiredRole = "access" },
            Bff = new() { ClientId = "test-client", ClientSecret = "test-only-secret" }
        };
        var oidc = new OpenIdConnectOptions
        {
            Backchannel = client,
            ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new()
            {
                Issuer = Issuer, TokenEndpoint = Issuer + "/token", SigningKeys = { Key }
            })
        };
        return new BffSessionRefresher(new Monitor(oidc), options, new BffAccessTokenClaimsValidator(options), TimeProvider.System);
    }

    private static HttpClient Client(HttpStatusCode status, string body) => new(new ResponseHandler(status, body));
    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsoluteUri.Should().Be(Issuer + "/token");
            (await request.Content!.ReadAsStringAsync(cancellationToken)).Should().Contain("grant_type=refresh_token");
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
    private sealed class Monitor(OpenIdConnectOptions options) : IOptionsMonitor<OpenIdConnectOptions>
    {
        public OpenIdConnectOptions CurrentValue => options;
        public OpenIdConnectOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<OpenIdConnectOptions, string?> listener) => null;
    }
}
