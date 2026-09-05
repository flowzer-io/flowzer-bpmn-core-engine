using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Prueft die konfigurierbare Authentifizierung (JWT Bearer / OIDC) und die CORS-Richtlinie der Web-API.
/// Die Tests ersetzen nur die OIDC-Metadatenabfrage durch einen statischen Signaturschluessel;
/// Handler, Policies und Claim-Aufloesung laufen wie in Produktion.
/// </summary>
[NonParallelizable]
public class AuthenticationAndCorsIntegrationTest
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-integration-test-signing-key-with-32-bytes+"));

    // Testzweck: Mit aktiviertem JWT-Bearer-Schema sind Fachendpunkte ohne Token nicht erreichbar (401),
    // auch solche, die bisher keinen Benutzerkontext verlangten (Definitionskatalog).
    [Test]
    public async Task ProtectedEndpoints_ShouldReturnUnauthorized_WhenJwtBearerIsEnabledAndNoTokenIsSent()
    {
        await using var factory = CreateJwtFactory(new TestStorage());
        using var client = factory.CreateClient();

        var definitions = await client.GetAsync("/definition/meta");
        var userTasks = await client.GetAsync("/usertask");
        var diagnostics = await client.GetAsync("/operations/diagnostics");

        definitions.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        userTasks.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        diagnostics.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Health-Endpunkte muessen fuer Orchestrator-Probes ohne Token erreichbar bleiben.
    [Test]
    public async Task HealthEndpoints_ShouldStayAnonymous_WhenJwtBearerIsEnabled()
    {
        await using var factory = CreateJwtFactory(new TestStorage());
        using var client = factory.CreateClient();

        var liveness = await client.GetAsync("/health");
        var readiness = await client.GetAsync("/health/ready");

        liveness.StatusCode.Should().Be(HttpStatusCode.OK);
        readiness.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein gueltiges Token des konfigurierten Issuers oeffnet die Fachendpunkte und liefert
    // die Benutzer-Id aus dem `sub`-Claim an die User-Task-Abfrage.
    [Test]
    public async Task UserTasks_ShouldResolveUserFromSubjectClaim_WhenValidTokenIsSent()
    {
        var storage = new TestStorage();
        var userId = Guid.NewGuid();
        await using var factory = CreateJwtFactory(storage);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId));

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>();
        payload!.Successful.Should().BeTrue();
        storage.LastRequestedUserTaskUserId.Should().Be(userId);
    }

    // Testzweck: Entra ID liefert die Benutzer-Id im Claim `oid`, nicht in `sub`. Der Claim muss
    // unter seinem Originalnamen ankommen (kein Inbound-Claim-Mapping) und als Benutzer-Id gelten.
    [Test]
    public async Task UserTasks_ShouldResolveUserFromOidClaim_WhenSubjectIsNotAGuid()
    {
        var storage = new TestStorage();
        var userId = Guid.NewGuid();
        await using var factory = CreateJwtFactory(storage);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(claims: [new Claim("sub", "opaque-subject-from-entra"), new Claim("oid", userId.ToString())]));

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.LastRequestedUserTaskUserId.Should().Be(userId);
    }

    // Testzweck: Ein Token eines fremden Issuers oder mit falscher Audience wird abgelehnt.
    [Test]
    public async Task ProtectedEndpoints_ShouldReturnUnauthorized_WhenTokenIssuerOrAudienceDoesNotMatch()
    {
        await using var factory = CreateJwtFactory(new TestStorage());
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(Guid.NewGuid(), issuer: "https://someone-else.test"));
        var wrongIssuer = await client.GetAsync("/definition/meta");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(Guid.NewGuid(), audience: "other-api"));
        var wrongAudience = await client.GetAsync("/definition/meta");

        wrongIssuer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongAudience.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Der technische Development-Header darf bei aktivem JWT-Bearer keinen Zugang mehr
    // verschaffen, auch nicht im Development-Environment.
    [Test]
    public async Task DevelopmentUserHeader_ShouldNotBypassAuthentication_WhenJwtBearerIsEnabled()
    {
        await using var factory = CreateJwtFactory(new TestStorage(), environmentName: "Development");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flowzer-UserId", Guid.NewGuid().ToString());

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Eine JWT-Bearer-Konfiguration ohne Authority oder Audience ist ein Betriebsfehler und
    // muss den Host-Start abbrechen statt still ohne Schutz zu laufen.
    [Test]
    public void Host_ShouldFailToStart_WhenJwtBearerIsEnabledWithoutAuthority()
    {
        var factory = new TestWebApplicationFactory(new TestStorage(), "Production", new Dictionary<string, string?>
        {
            ["Authentication:Scheme"] = "JwtBearer",
            ["Authentication:JwtBearer:Audience"] = Audience
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<Exception>().Where(exception =>
            exception.ToString().Contains("Authentication:JwtBearer:Authority", StringComparison.Ordinal));
    }

    // Testzweck: Ohne CORS-Konfiguration sendet die API ausserhalb von Development keine
    // Access-Control-Allow-Origin-Header mehr; die bisherige Wildcard galt fuer jede Umgebung.
    [Test]
    public async Task Cors_ShouldNotAllowAnyOrigin_WhenNothingIsConfiguredOutsideDevelopment()
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage(), "Production");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://console.example");

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    // Testzweck: Konfigurierte Origins werden exakt zugelassen, alle anderen nicht.
    [Test]
    public async Task Cors_ShouldAllowOnlyConfiguredOrigins()
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage(), "Production", new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://console.example"
        });
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("Origin", "https://console.example");
        var allowed = await client.GetAsync("/health");
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        var denied = await client.GetAsync("/health");

        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://console.example");
        denied.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    // Testzweck: Im Development-Environment bleibt der bisherige Komfort erhalten: ohne Konfiguration
    // wird jede Origin zugelassen, damit Blazor-Dev-Server und Playwright weiter funktionieren.
    [Test]
    public async Task Cors_ShouldAllowAnyOrigin_WhenNothingIsConfiguredInDevelopment()
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage(), "Development");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://console.example");

        var response = await client.GetAsync("/health");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("*");
    }

    private static TestWebApplicationFactory CreateJwtFactory(TestStorage storage, string environmentName = "Production")
    {
        return new TestWebApplicationFactory(storage, environmentName, new Dictionary<string, string?>
        {
            ["Authentication:Scheme"] = "JwtBearer",
            ["Authentication:JwtBearer:Authority"] = Issuer,
            ["Authentication:JwtBearer:Audience"] = Audience
        }, useStaticSigningKey: true);
    }

    private static string CreateToken(Guid userId, string issuer = Issuer, string audience = Audience)
    {
        return CreateToken([new Claim("sub", userId.ToString())], issuer, audience);
    }

    private static string CreateToken(Claim[] claims, string issuer = Issuer, string audience = Audience)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims)
        });
    }

    private sealed class TestWebApplicationFactory(
        TestStorage storage,
        string environmentName,
        IReadOnlyDictionary<string, string?>? configuration = null,
        bool useStaticSigningKey = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, environmentName);

            // Auth und CORS werden in Program.cs zur Registrierungszeit aus builder.Configuration
            // gelesen. Werte aus ConfigureAppConfiguration sind dort noch nicht sichtbar, Host-Settings
            // (UseSetting) dagegen schon.
            builder.UseSetting("TimerScheduler:Enabled", "false");
            foreach (var entry in configuration ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(entry.Key, entry.Value);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStorageSystem>();
                services.RemoveAll<ITransactionalStorageProvider>();
                services.AddSingleton<IStorageSystem>(storage);
                services.AddSingleton<ITransactionalStorageProvider>(new TestTransactionalStorageProvider(storage));

                if (useStaticSigningKey)
                {
                    // Ersetzt ausschliesslich die OIDC-Discovery (Netzwerkzugriff auf die Authority)
                    // durch statische Metadaten mit dem Testschluessel. Alles andere bleibt produktiv.
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                            new OpenIdConnectConfiguration
                            {
                                Issuer = Issuer,
                                SigningKeys = { SigningKey }
                            });
                    });
                }
            });
        }
    }

    private sealed class TestTransactionalStorageProvider(TestStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => storage;
    }

    private sealed class TestStorage : ITransactionalStorage
    {
        public Guid? LastRequestedUserTaskUserId { get; set; }

        public IDefinitionStorage DefinitionStorage => new EmptyDefinitionStorage();
        public IMessageSubscriptionStorage SubscriptionStorage => new EmptySubscriptionStorage(this);
        public IInstanceStorage InstanceStorage => new EmptyInstanceStorage();
        public IFormStorage FormStorage => new EmptyFormStorage();

        public void CommitChanges()
        {
        }

        public void RollbackTransaction()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class EmptyDefinitionStorage : IDefinitionStorage
    {
        public Task StoreBinary(Guid guid, string data) => throw new NotSupportedException();
        public Task<string> GetBinary(Guid guid) => throw new NotSupportedException();
        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(Array.Empty<Guid>());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(Array.Empty<BpmnDefinition>());
        public Task StoreDefinition(BpmnDefinition definition) => throw new NotSupportedException();
        public Task<Model.Version?> GetMaxVersionId(string modelId) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();
        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) => throw new NotSupportedException();
        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => throw new NotSupportedException();
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => throw new NotSupportedException();
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new NotSupportedException();
    }

    private sealed class EmptySubscriptionStorage(TestStorage storage) : IMessageSubscriptionStorage
    {
        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task AddMessageSubscription(MessageSubscription messageSubscription) => Task.CompletedTask;
        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId) => Task.CompletedTask;
        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;

        public void AddSignalSubscription(SignalSubscription signalSubscription)
        {
        }

        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<SignalSubscription>());

        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId)
        {
        }

        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) => Task.FromResult(Enumerable.Empty<UserTaskSubscription>());

        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId)
        {
            storage.LastRequestedUserTaskUserId = userId;
            return Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());
        }

        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;
        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;

        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId)
        {
        }

        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task AddTimerSubscription(TimerSubscription timerSubscription) => Task.CompletedTask;
        public Task RemoveTimerSubscription(Guid timerSubscriptionId) => Task.CompletedTask;
        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class EmptyInstanceStorage : IInstanceStorage
    {
        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) => throw new FileNotFoundException();
        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo) => Task.CompletedTask;
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() => Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() => Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
    }

    private sealed class EmptyFormStorage : IFormStorage
    {
        public Task SaveFormMetaData(FormMetadata formMetadata) => Task.CompletedTask;
        public Task<FormMetadata> GetFormMetaData(Guid formId) => throw new FileNotFoundException();
        public Task<IEnumerable<FormMetadata>> GetFormMetadatas() => Task.FromResult(Enumerable.Empty<FormMetadata>());
        public Task UpdateFormMetaData(FormMetadata formMetaData) => Task.CompletedTask;
        public Task DeleteFormMetaData(Guid formId) => Task.CompletedTask;
        public Task SaveForm(Form form) => Task.CompletedTask;
        public Task<Form> GetForm(Guid id) => throw new FileNotFoundException();
        public Task<IEnumerable<Form>> GetForms(Guid formId) => Task.FromResult(Enumerable.Empty<Form>());
        public Task DeleteForm(Guid id) => Task.CompletedTask;
        public Task<Model.Version> GetMaxVersion(Guid formId) => Task.FromResult(new Model.Version());
    }
}
