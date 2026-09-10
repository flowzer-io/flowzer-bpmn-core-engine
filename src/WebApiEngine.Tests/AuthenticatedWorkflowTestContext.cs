using System.Dynamic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
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
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Isolierte Dateiablage, echte Engine und signierte synthetische Identitäten für Sicherheitsfälle.</summary>
internal sealed class AuthenticatedWorkflowTestContext : IDisposable
{
    internal static readonly Guid UserId = Guid.Parse("bf68c19d-6a91-4086-a201-b4ea660bd957");
    internal const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";
    // Ausschließlich synthetischer Signaturschlüssel für den lokalen Test-IdP.
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-test-only-signing-key-32-bytes-or-more"));
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "flowzer-completion-test", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    internal AuthenticatedWorkflowTestContext()
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
            builder.UseSetting("Authentication:Scheme", "JwtBearer");
            builder.UseSetting("Authentication:JwtBearer:Authority", Issuer);
            builder.UseSetting("Authentication:JwtBearer:Audience", Audience);
            builder.UseSetting("Authentication:JwtBearer:Roles:Operator", "operator");
            builder.UseSetting("Authentication:JwtBearer:Roles:Modeler", "modeler");
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuers = [Issuer, Issuer + "-second"];
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } });
                }));
        });
    }

    internal Storage Storage { get; }
    internal IServiceProvider Services => _factory.Services;

    internal HttpClient CreateClient(bool isOperator = false, Guid? userId = null, string username = "bert", bool isModeler = false, string? issuer = null)
    {
        var claims = new List<Claim>
        {
            new("sub", (userId ?? UserId).ToString()), new("preferred_username", username),
            new("groups", "/team/review")
        };
        if (isOperator) claims.Add(new Claim("roles", "operator"));
        if (isModeler) claims.Add(new Claim("roles", "modeler"));
        var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims)
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    internal async Task<UserTaskSubscription> StartAsync(
        string assignment,
        ExpandoObject? variables = null,
        string? assignmentExtensionXml = null,
        string? taskScheduleXml = null)
    {
        var definition = await DeployAsync(assignment, assignmentExtensionXml: assignmentExtensionXml,
            taskScheduleXml: taskScheduleXml);
        var engine = Services.GetRequiredService<BpmnBusinessLogic>();
        var instance = await engine.StartProcessInstance(definition.DefinitionId, variables);
        return (await Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
    }

    internal async Task<BpmnDefinition> DeployAsync(
        string assignment,
        string? startFormKey = null,
        string? assignmentExtensionXml = null,
        string? taskScheduleXml = null)
    {
        await FormTestSeed.StoreAsync(Storage, "Approval");
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(), DefinitionId = "Definitions_Completion", Hash = "test",
            SavedByUser = UserId, SavedOn = DateTime.UtcNow, Version = new Model.Version(1, 0), IsActive = false
        };
        await Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = definition.DefinitionId, Name = "Review" });
        await Storage.DefinitionStorage.StoreDefinition(definition);
        var startForm = startFormKey is null ? "" : $"<bpmn:extensionElements><zeebe:formDefinition formKey=\"{startFormKey}\" /></bpmn:extensionElements>";
        var assignmentElement = assignmentExtensionXml
                                ?? $"<zeebe:assignmentDefinition {assignment} />";
        await Storage.DefinitionStorage.StoreBinary(definition.Id, $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                id="Definitions_Completion" targetNamespace="test">
              <bpmn:process id="Process_Completion" isExecutable="true">
                <bpmn:startEvent id="Start">{{startForm}}<bpmn:outgoing>ToReview</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="ToReview" sourceRef="Start" targetRef="Review" />
                <bpmn:userTask id="Review" name="Review">
                  <bpmn:extensionElements>
                    <zeebe:formDefinition formKey="Approval" />
                    {{assignmentElement}}
                    {{taskScheduleXml ?? ""}}
                  </bpmn:extensionElements>
                  <bpmn:incoming>ToReview</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
                </bpmn:userTask>
                <bpmn:sequenceFlow id="ToEnd" sourceRef="Review" targetRef="End" />
                <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """);
        var engine = _factory.Services.GetRequiredService<BpmnBusinessLogic>();
        await engine.DeployDefinition(definition);
        return definition;
    }

    internal async Task AssertStillActiveAsync(UserTaskSubscription task)
    {
        var instance = await Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
        instance.IsFinished.Should().BeFalse();
        var token = instance.Tokens.Single(candidate => candidate.Id == task.Token.Id);
        token.State.Should().Be(FlowNodeState.Active);
        token.OutputData.Should().BeNull();
        (await Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single().Id.Should().Be(task.Id);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
