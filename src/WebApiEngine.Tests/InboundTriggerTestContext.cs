using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FilesystemStorageSystem;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Model;
using WebApiEngine.BusinessLogic;
using WebApiEngine.InboundTriggers;

namespace WebApiEngine.Tests;

/// <summary>
/// Isolierte Dateiablage, echte Engine und signierte synthetische Identitäten für die Auslöser.
/// Der installationsweite Schlüssel ist gesetzt; ohne ihn nimmt die Installation bewusst keine
/// Auslöser an.
/// </summary>
internal sealed class InboundTriggerTestContext : IDisposable
{
    internal const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";

    /// <summary>Ausschließlich synthetisches Schlüsselmaterial für den lokalen Test-IdP.</summary>
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-trigger-test-signing-key-32-bytes-or-more"));

    internal const string InstallationKey = "flowzer-trigger-test-installation-key-0123456789";

    private readonly string? _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowzer-trigger-test", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    internal InboundTriggerTestContext(IDictionary<string, string?>? settings = null)
    {
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
        Storage = new Storage();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("TimerScheduler:Enabled", "false");
            builder.UseSetting("UserTaskDeadlines:Enabled", "false");
            builder.UseSetting("ServiceTaskWebhooks:Enabled", "false");
            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("InboundTriggers:SecretKey", InstallationKey);
            builder.UseSetting("Authentication:Scheme", "JwtBearer");
            builder.UseSetting("Authentication:JwtBearer:Authority", Issuer);
            builder.UseSetting("Authentication:JwtBearer:Audience", Audience);
            builder.UseSetting("Authentication:JwtBearer:RequiredRole", "access");
            builder.UseSetting("Authentication:JwtBearer:Roles:Operator", "operator");
            builder.UseSetting("Authentication:JwtBearer:Roles:Modeler", "modeler");
            builder.UseSetting("Authentication:JwtBearer:Roles:Worker", "worker");
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuers = [Issuer];
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } });
                }));
        });
    }

    internal Storage Storage { get; }
    internal IServiceProvider Services => _factory.Services;

    /// <summary>Ein Aufrufer mit der Betriebsrolle — die Verwaltung gehört dem Betrieb.</summary>
    internal HttpClient CreateOperatorClient() => CreateClient(isOperator: true);

    /// <summary>Angemeldet, aber ohne Betriebsrolle.</summary>
    internal HttpClient CreatePlainClient() => CreateClient(isOperator: false);

    /// <summary>Ohne jede Anmeldung — so ruft ein fremdes System den Auslöser auf.</summary>
    internal HttpClient CreateAnonymousClient() => _factory.CreateClient();

    private HttpClient CreateClient(bool isOperator)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()), new("preferred_username", "bert"), new("roles", "access")
        };
        if (isOperator) claims.Add(new Claim("roles", "operator"));
        var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims)
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    /// <summary>Ein Workflow, der ohne Startformular sofort durchläuft.</summary>
    internal Task<string> DeployStartWorkflowAsync(string definitionId = "trigger-start") => DeployAsync(
        definitionId,
        $$"""
          <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
              id="Definitions_{{definitionId}}" targetNamespace="test">
            <bpmn:process id="Process_{{definitionId}}" isExecutable="true">
              <bpmn:startEvent id="Start"><bpmn:outgoing>ToWait</bpmn:outgoing></bpmn:startEvent>
              <bpmn:sequenceFlow id="ToWait" sourceRef="Start" targetRef="Work" />
              <bpmn:serviceTask id="Work" name="Work">
                <bpmn:extensionElements><zeebe:taskDefinition type="trigger-work" /></bpmn:extensionElements>
                <bpmn:incoming>ToWait</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
              </bpmn:serviceTask>
              <bpmn:sequenceFlow id="ToEnd" sourceRef="Work" targetRef="End" />
              <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
            </bpmn:process>
          </bpmn:definitions>
          """);

    /// <summary>
    /// Ein Workflow, der an einem Nachrichtenereignis wartet. Der Korrelationsschlüssel ist ein
    /// Ausdruck über der Prozessvariablen <c>orderId</c>.
    /// </summary>
    internal Task<string> DeployMessageWorkflowAsync(string definitionId = "trigger-message") => DeployAsync(
        definitionId,
        $$"""
          <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
              id="Definitions_{{definitionId}}" targetNamespace="test">
            <bpmn:message id="Message_OrderPaid" name="OrderPaid">
              <bpmn:extensionElements><zeebe:subscription correlationKey="=orderId" /></bpmn:extensionElements>
            </bpmn:message>
            <bpmn:process id="Process_{{definitionId}}" isExecutable="true">
              <bpmn:startEvent id="Start"><bpmn:outgoing>ToAwait</bpmn:outgoing></bpmn:startEvent>
              <bpmn:sequenceFlow id="ToAwait" sourceRef="Start" targetRef="Await" />
              <bpmn:intermediateCatchEvent id="Await">
                <bpmn:messageEventDefinition messageRef="Message_OrderPaid" />
                <bpmn:incoming>ToAwait</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
              </bpmn:intermediateCatchEvent>
              <bpmn:sequenceFlow id="ToEnd" sourceRef="Await" targetRef="End" />
              <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
            </bpmn:process>
          </bpmn:definitions>
          """);

    private async Task<string> DeployAsync(string definitionId, string xml)
    {
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(),
            DefinitionId = definitionId,
            Hash = "test",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = false
        };
        await Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = definitionId, Name = definitionId });
        await Storage.DefinitionStorage.StoreDefinition(definition);
        await Storage.DefinitionStorage.StoreBinary(definition.Id, xml);
        await Services.GetRequiredService<BpmnBusinessLogic>().DeployDefinition(definition);
        return definitionId;
    }

    /// <summary>Legt einen Katalogeintrag ohne deployte Version an — für den 422-Fall.</summary>
    internal async Task<string> RegisterUndeployedWorkflowAsync(string definitionId = "trigger-undeployed")
    {
        await Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = definitionId, Name = definitionId });
        return definitionId;
    }

    /// <summary>
    /// Baut den Aufruf so, wie ein fremdes System ihn schicken würde: roher Körper, Zeitstempel
    /// und die Signatur darüber.
    /// </summary>
    internal static HttpRequestMessage SignedRequest(
        string key,
        string secret,
        string body,
        DateTimeOffset? sentAt = null,
        string? signatureOverride = null,
        string? idempotencyKey = null)
    {
        var timestamp = (sentAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/trigger/{key}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(InboundTriggerSecret.TimestampHeader, timestamp.ToString());
        request.Headers.TryAddWithoutValidation(
            InboundTriggerSecret.SignatureHeader,
            signatureOverride ?? InboundTriggerSecret.ComputeSignature(secret, timestamp, body));
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
