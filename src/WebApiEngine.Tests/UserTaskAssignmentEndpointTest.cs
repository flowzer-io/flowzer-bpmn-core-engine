using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BPMN.Common;
using BPMN.HumanInteraction;
using BPMN.Process;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Model;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Die Zuweisung muss an jedem Endpunkt gelten, der eine Aufgabe betrifft. Sie nur beim
/// Auflisten zu pruefen genuegt nicht: Abschliessen ist der eigentliche Eingriff.
/// </summary>
[NonParallelizable]
public class UserTaskAssignmentEndpointTest
{
    // Testzweck: Eine fremd zugewiesene Aufgabe erscheint nicht in der Liste.
    [Test]
    public async Task TaskList_ShouldHideTasksAssignedToSomeoneElse()
    {
        using var context = new AssignmentEndpointContext();
        await context.AddUserTask(assignee: "anna");
        using var client = context.CreateClient();

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>();
        payload!.Result.Should().BeEmpty();
    }

    // Testzweck: Dieselbe Aufgabe laesst sich auch nicht abschliessen, wenn Token und
    // Flow-Node bekannt sind. Ohne diese Pruefung genuegte die Kenntnis der Ids.
    [Test]
    public async Task CompletingATask_ShouldBeRefused_WhenItIsAssignedToSomeoneElse()
    {
        using var context = new AssignmentEndpointContext();
        var subscription = await context.AddUserTask(assignee: "anna");
        using var client = context.CreateClient();

        var response = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            FlowNodeId = "UserTask_1",
            TokenId = subscription.Token.Id,
            ProcessInstanceId = subscription.ProcessInstanceId
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ohne Zuweisung bleibt der Abschluss erreichbar; die Pruefung darf den
    // bisherigen Normalfall nicht blockieren.
    [Test]
    public async Task CompletingATask_ShouldStillReachTheEngine_WhenNobodyIsAssigned()
    {
        using var context = new AssignmentEndpointContext();
        var subscription = await context.AddUserTask(assignee: null);
        using var client = context.CreateClient();

        var response = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            FlowNodeId = "UserTask_1",
            TokenId = subscription.Token.Id,
            ProcessInstanceId = subscription.ProcessInstanceId
        });

        // Die Instanz existiert in dieser Ablage nicht, die Engine antwortet also mit einem
        // eigenen Fehler. Entscheidend ist, dass es nicht die Zuweisungspruefung war.
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult>();
        (payload?.ErrorMessage ?? string.Empty).Should().NotContain("User task for token");
    }

    private sealed class AssignmentEndpointContext : IDisposable
    {
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;
        private readonly Guid _definitionId = Guid.NewGuid();
        private const string MetaDefinitionId = "catalog-assignment";

        public AssignmentEndpointContext()
        {
            _previousStorageRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(Path.GetTempPath(), "flowzer-assignment-endpoint-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _storageRoot);

            Storage = new Storage();
            Factory = new AssignmentFactory();
        }

        public Storage Storage { get; }
        public AssignmentFactory Factory { get; }

        /// <summary>
        /// Meldet eine Person an, die weder genannt noch im Betrieb ist. Ohne Identity Provider
        /// gaebe es keine Rollen, und die Betriebssicht waere fuer alle offen; die Zuweisung
        /// liesse sich dann gar nicht pruefen.
        /// </summary>
        public HttpClient CreateClient(string userName = "bert")
        {
            var client = Factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userName));
            return client;
        }

        public async Task<UserTaskSubscription> AddUserTask(string? assignee)
        {
            await Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = MetaDefinitionId,
                Name = "Freigabeprozess"
            });
            await Storage.DefinitionStorage.StoreDefinition(new BpmnDefinition
            {
                Id = _definitionId,
                DefinitionId = MetaDefinitionId,
                Hash = "hash",
                SavedByUser = Guid.NewGuid(),
                SavedOn = DateTime.UtcNow,
                Version = new Model.Version(1, 0),
                IsActive = true
            });

            var userTask = new UserTask { Id = "UserTask_1", Name = "Freigabe", Implementation = "Formular" };
            var subscription = new UserTaskSubscription
            {
                Id = Guid.NewGuid(),
                Name = "Freigabe",
                Token = new Token
                {
                    ProcessInstanceId = Guid.NewGuid(),
                    CurrentBaseElement = userTask,
                    ActiveBoundaryEvents = [],
                    State = FlowNodeState.Active
                },
                MetaDefinitionId = MetaDefinitionId,
                DefinitionId = _definitionId,
                ProcessId = "Process_1",
                Assignee = assignee
            };
            subscription.ProcessInstanceId = subscription.Token.ProcessInstanceId;

            await Storage.SubscriptionStorage.AddUserTaskSubscription(subscription);
            return subscription;
        }

        public void Dispose()
        {
            Factory.Dispose();
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);

            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
    }

    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-integration-test-signing-key-with-32-bytes+"));

    private static string CreateToken(string userName)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("preferred_username", userName)
            ])
        });
    }

    /// <summary>
    /// Laeuft mit aktivem JWT-Schema und konfigurierter Betriebsrolle, damit die aufrufende
    /// Person die Betriebssicht nicht automatisch hat.
    /// </summary>
    public sealed class AssignmentFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Production");
            builder.UseSetting("TimerScheduler:Enabled", "false");
            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("Authentication:Scheme", "JwtBearer");
            builder.UseSetting("Authentication:JwtBearer:Authority", Issuer);
            builder.UseSetting("Authentication:JwtBearer:Audience", Audience);
            builder.UseSetting("Authentication:JwtBearer:Roles:Operator", "operator");

            builder.ConfigureServices(services =>
            {
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
    }
}
