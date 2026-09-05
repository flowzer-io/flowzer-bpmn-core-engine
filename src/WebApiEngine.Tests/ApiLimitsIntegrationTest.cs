using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Schutz gegen einzelne Aufrufer, die den Dienst fluten oder mit riesigen Uploads belegen.
/// Beides war bisher unbegrenzt; ein einzelner Client konnte den Betrieb fuer alle stoeren.
/// </summary>
[NonParallelizable]
public class ApiLimitsIntegrationTest
{
    // Testzweck: Ueber dem konfigurierten Kontingent antwortet die API 429 mit Retry-After
    // und dem ueblichen Fehlervertrag statt die Last einfach anzunehmen.
    [Test]
    public async Task Requests_ShouldBeRejectedWith429_WhenTheRateLimitIsExceeded()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:PermitLimit"] = "3",
            ["RateLimiting:WindowSeconds"] = "60"
        });
        using var client = factory.CreateClient();

        var statusCodes = new List<HttpStatusCode>();
        HttpResponseMessage? limited = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.GetAsync("/definition/meta");
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited ??= response;
            }
        }

        statusCodes.Take(3).Should().AllBeEquivalentTo(HttpStatusCode.OK);
        statusCodes.Should().Contain(HttpStatusCode.TooManyRequests);
        limited!.Headers.RetryAfter.Should().NotBeNull();
        var payload = await limited.Content.ReadFromJsonAsync<ApiStatusResult>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    // Testzweck: Orchestrator-Probes duerfen nie am Kontingent scheitern, sonst startet
    // ein Deployment unter Last nicht mehr durch.
    [Test]
    public async Task HealthEndpoints_ShouldStayExemptFromTheRateLimit()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:PermitLimit"] = "1",
            ["RateLimiting:WindowSeconds"] = "60"
        });
        using var client = factory.CreateClient();

        var results = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            results.Add((await client.GetAsync("/health/ready")).StatusCode);
        }

        results.Should().AllBeEquivalentTo(HttpStatusCode.OK);
    }

    // Testzweck: Ein Upload ueber der konfigurierten Groesse wird abgewiesen, bevor die
    // Engine ihn parst; sonst zieht eine einzige Anfrage den Speicher des Hosts leer.
    [Test]
    public async Task DefinitionUpload_ShouldBeRejectedWith413_WhenItExceedsTheConfiguredSize()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Limits:MaxUploadBytes"] = "1024"
        });
        using var client = factory.CreateClient();
        var oversized = new StringContent(new string('x', 4096), Encoding.UTF8, "application/xml");

        var response = await client.PostAsync("/definition", oversized);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult>();
        payload!.Successful.Should().BeFalse();
    }

    // Testzweck: Unterhalb der Grenze bleibt der Upload-Pfad unveraendert erreichbar; die
    // Begrenzung darf nicht jeden Upload abweisen.
    [Test]
    public async Task DefinitionUpload_ShouldStillReachTheEngine_WhenItStaysBelowTheLimit()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Limits:MaxUploadBytes"] = "65536"
        });
        using var client = factory.CreateClient();
        var small = new StringContent("<definitions/>", Encoding.UTF8, "application/xml");

        var response = await client.PostAsync("/definition", small);

        // Der Inhalt ist kein gueltiges BPMN; entscheidend ist, dass die Groessenpruefung
        // ihn durchlaesst und die Engine ihn fachlich beurteilt.
        response.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge);
    }

    // Testzweck: Das Kontingent gilt je angemeldeter Person. Wuerde es vor der Authentifizierung
    // greifen, fielen alle in dieselbe Partition und eine Person koennte alle anderen aussperren.
    [Test]
    public async Task RateLimit_ShouldBeCountedPerAuthenticatedPerson()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:PermitLimit"] = "2",
            ["RateLimiting:WindowSeconds"] = "60",
            ["Authentication:Scheme"] = "JwtBearer",
            ["Authentication:JwtBearer:Authority"] = Issuer,
            ["Authentication:JwtBearer:Audience"] = Audience
        });
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        var first = await client.GetAsync("/definition/meta");
        var second = await client.GetAsync("/definition/meta");
        var third = await client.GetAsync("/definition/meta");

        // Eine zweite Person hat ihr eigenes Kontingent und ist von der ersten unberuehrt.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        var otherPerson = await client.GetAsync("/definition/meta");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        otherPerson.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ohne Content-Length (chunked) greift die Groessengrenze trotzdem; sonst waere
    // sie mit einem einzigen Header-Weglassen umgangen.
    [Test]
    public async Task DefinitionUpload_ShouldBeRejected_WhenItExceedsTheLimitWithoutContentLength()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Limits:MaxUploadBytes"] = "1024"
        });
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/definition")
        {
            Content = new ChunkedContent(new string('x', 8192))
        };
        var response = await client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-integration-test-signing-key-with-32-bytes+"));

    private static string CreateToken()
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())])
        });
    }

    /// <summary>Sendet ohne Content-Length, damit die Fruehabsage am Header nicht greifen kann.</summary>
    private sealed class ChunkedContent(string payload) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = Encoding.UTF8.GetBytes(payload);
            await stream.WriteAsync(buffer);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static TestWebApplicationFactory CreateFactory(IReadOnlyDictionary<string, string?> configuration)
    {
        return new TestWebApplicationFactory(configuration);
    }

    /// <summary>
    /// Nutzt die echte Dateiablage unter einem Wegwerf-Verzeichnis. Die Grenzen greifen in der
    /// Pipeline vor den Controllern; ein Ablage-Ersatz wuerde davon nichts zeigen.
    /// </summary>
    private sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly IReadOnlyDictionary<string, string?> _configuration;
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;

        public TestWebApplicationFactory(IReadOnlyDictionary<string, string?> configuration)
        {
            _configuration = configuration;
            _previousStorageRoot = Environment.GetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(Path.GetTempPath(), "flowzer-limits-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName, _storageRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Development");
            builder.UseSetting("TimerScheduler:Enabled", "false");
            foreach (var entry in _configuration)
            {
                builder.UseSetting(entry.Key, entry.Value);
            }

            builder.ConfigureServices(services =>
            {
                // Ersetzt ausschliesslich die OIDC-Discovery durch statische Metadaten.
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
