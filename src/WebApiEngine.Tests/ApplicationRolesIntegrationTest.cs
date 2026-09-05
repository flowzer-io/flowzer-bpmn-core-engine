using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace WebApiEngine.Tests;

/// <summary>
/// Anwendungsrollen oberhalb der reinen Zugangsrolle: Wer Modelle veroeffentlichen oder den
/// Betrieb einsehen darf, ist eine andere Frage als wer Flowzer ueberhaupt benutzen darf.
/// </summary>
[NonParallelizable]
public class ApplicationRolesIntegrationTest
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-integration-test-signing-key-with-32-bytes+"));

    // Testzweck: Ohne Modelliererrolle ist das Veroeffentlichen gesperrt, das Lesen aber nicht.
    [Test]
    public async Task Deploying_ShouldRequireTheModelerRole()
    {
        await using var factory = CreateFactory(modelerRole: "modeler", operatorRole: "operator");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken());

        var deploy = await client.PostAsync("/definition/deploy", new StringContent("<definitions/>", Encoding.UTF8, "application/xml"));
        var read = await client.GetAsync("/definition/meta");

        deploy.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Mit der Rolle kommt derselbe Aufruf bis zur Engine durch.
    [Test]
    public async Task Deploying_ShouldReachTheEngine_WhenTheModelerRoleIsPresent()
    {
        await using var factory = CreateFactory(modelerRole: "modeler", operatorRole: "operator");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));

        var response = await client.PostAsync("/definition/deploy", new StringContent("<definitions/>", Encoding.UTF8, "application/xml"));

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // Testzweck: Die Diagnose ist dem Betrieb vorbehalten.
    [Test]
    public async Task Diagnostics_ShouldRequireTheOperatorRole()
    {
        await using var factory = CreateFactory(modelerRole: "modeler", operatorRole: "operator");
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken());
        var withoutRole = await client.GetAsync("/operations/diagnostics");

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["operator"]));
        var withRole = await client.GetAsync("/operations/diagnostics");

        withoutRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        withRole.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ohne konfigurierte Rollennamen bleibt alles wie bisher offen; bestehende
    // Installationen duerfen durch das Update nicht ausgesperrt werden.
    [Test]
    public async Task Endpoints_ShouldStayOpen_WhenNoApplicationRolesAreConfigured()
    {
        await using var factory = CreateFactory(modelerRole: null, operatorRole: null);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken());

        var diagnostics = await client.GetAsync("/operations/diagnostics");
        var deploy = await client.PostAsync("/definition/deploy", new StringContent("<definitions/>", Encoding.UTF8, "application/xml"));

        diagnostics.StatusCode.Should().Be(HttpStatusCode.OK);
        deploy.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    // Testzweck: Eine Rollen-Policy am Endpunkt ersetzt die Fallback-Policy. Anmeldepflicht und
    // Zugangsrolle muessen deshalb auch auf diesen Endpunkten weiter gelten.
    [Test]
    public async Task RoleProtectedEndpoints_ShouldStillRequireAuthenticationAndTheAccessRole()
    {
        await using var factory = CreateFactory(modelerRole: "modeler", operatorRole: "operator", requiredRole: "access");
        using var client = factory.CreateClient();

        var withoutToken = await client.GetAsync("/operations/diagnostics");

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["operator"]));
        var withoutAccessRole = await client.GetAsync("/operations/diagnostics");

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["access", "operator"]));
        var complete = await client.GetAsync("/operations/diagnostics");

        withoutToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        withoutAccessRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static string CreateToken(string[]? roles = null)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        claims.AddRange((roles ?? []).Select(role => new Claim("roles", role)));

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims)
        });
    }

    private static RolesTestFactory CreateFactory(string? modelerRole, string? operatorRole, string? requiredRole = null) =>
        new(modelerRole, operatorRole, requiredRole);

    private sealed class RolesTestFactory : WebApplicationFactory<Program>
    {
        private readonly string? _modelerRole;
        private readonly string? _operatorRole;
        private readonly string? _requiredRole;
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;

        public RolesTestFactory(string? modelerRole, string? operatorRole, string? requiredRole)
        {
            _modelerRole = modelerRole;
            _operatorRole = operatorRole;
            _requiredRole = requiredRole;
            _previousStorageRoot = Environment.GetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(Path.GetTempPath(), "flowzer-roles-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName, _storageRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Production");
            builder.UseSetting("TimerScheduler:Enabled", "false");
            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("Authentication:Scheme", "JwtBearer");
            builder.UseSetting("Authentication:JwtBearer:Authority", Issuer);
            builder.UseSetting("Authentication:JwtBearer:Audience", Audience);

            if (_requiredRole is not null)
            {
                builder.UseSetting("Authentication:JwtBearer:RequiredRole", _requiredRole);
            }

            if (_modelerRole is not null)
            {
                builder.UseSetting("Authentication:JwtBearer:Roles:Modeler", _modelerRole);
            }

            if (_operatorRole is not null)
            {
                builder.UseSetting("Authentication:JwtBearer:Roles:Operator", _operatorRole);
            }

            builder.ConfigureServices(services =>
            {
                // Ersetzt ausschliesslich die OIDC-Discovery durch statische Metadaten mit dem
                // Testschluessel; Handler, Policies und Rollenauswertung laufen wie in Produktion.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration
                        {
                            Issuer = Issuer,
                            SigningKeys = { SigningKey }
                        });
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            Environment.SetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
    }
}
