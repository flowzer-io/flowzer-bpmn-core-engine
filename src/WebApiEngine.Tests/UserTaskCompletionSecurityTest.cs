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

/// <summary>
/// Prüft beide öffentlichen Abschlusswege mit signierten Testidentitäten und einer
/// tatsächlich gestarteten Instanz. Ein HTTP-Fehler allein genügt nicht: Bei Ablehnung
/// müssen Token und Subscription unverändert bleiben.
/// </summary>
[NonParallelizable]
public class UserTaskCompletionSecurityTest
{
    // Testzweck: Auch der ältere Formular-Endpunkt darf fremde Aufgaben nicht abschließen.
    [TestCase("/usertask")]
    [TestCase("/form/result")]
    public async Task Completion_ShouldRejectAnotherUsersTaskWithoutMutatingState(string route)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient();

        using var response = await client.PostAsJsonAsync(route, ResultFor(task));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await context.AssertStillActiveAsync(task);
    }

    // Testzweck: Die zentrale Prüfung erhält alle bestehenden erlaubten Zuweisungsarten.
    [TestCase("/usertask", "")]
    [TestCase("/form/result", "")]
    [TestCase("/usertask", "assignee=\"bert\"")]
    [TestCase("/form/result", "assignee=\"bert\"")]
    [TestCase("/usertask", "candidateUsers=\"anna,bert\"")]
    [TestCase("/form/result", "candidateUsers=\"anna,bert\"")]
    [TestCase("/usertask", "candidateGroups=\"/team/review\"")]
    [TestCase("/form/result", "candidateGroups=\"/team/review\"")]
    public async Task Completion_ShouldAllowAnEligibleUser(string route, string assignment)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync(assignment);
        using var client = context.CreateClient();

        using var response = await client.PostAsJsonAsync(route, ResultFor(task));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var instance = await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
        instance.IsFinished.Should().BeTrue();
        (await context.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().BeEmpty();
    }

    // Testzweck: Der ausdrücklich berechtigte Operator darf eine fremde Aufgabe bearbeiten.
    [TestCase("/usertask")]
    [TestCase("/form/result")]
    public async Task Completion_ShouldAllowAnOperator(string route)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient(isOperator: true);

        using var response = await client.PostAsJsonAsync(route, ResultFor(task));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Fehlende Subscriptions sind keine Erlaubnis, nur mit bekannten Token-IDs
    // direkt die Engine aufzurufen; auch ein Operator muss eine gültige Aufgabe adressieren.
    [TestCase("/usertask", false)]
    [TestCase("/form/result", false)]
    [TestCase("/usertask", true)]
    [TestCase("/form/result", true)]
    public async Task Completion_ShouldFailClosedWhenSubscriptionIsMissing(string route, bool isOperator)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("");
        await context.Storage.SubscriptionStorage.RemoveUserTaskSubscription(task.Id);
        using var client = context.CreateClient(isOperator);

        using var response = await client.PostAsJsonAsync(route, ResultFor(task));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value))
            .Tokens.Single(token => token.Id == task.Token.Id).State.Should().Be(FlowNodeState.Active);
    }

    // Testzweck: Instanz, Token und BPMN-Schritt bilden eine Einheit; Manipulationen dürfen
    // weder ausgeführt werden noch durch unterschiedliche Fehler fremde Aufgaben offenlegen.
    [TestCase("/usertask", "instance")]
    [TestCase("/form/result", "instance")]
    [TestCase("/usertask", "token")]
    [TestCase("/form/result", "token")]
    [TestCase("/usertask", "node")]
    [TestCase("/form/result", "node")]
    public async Task Completion_ShouldRejectInconsistentIdentifiers(string route, string changedIdentifier)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("");
        var result = ResultFor(task);
        if (changedIdentifier == "instance") result.ProcessInstanceId = Guid.NewGuid();
        if (changedIdentifier == "token") result.TokenId = Guid.NewGuid();
        if (changedIdentifier == "node") result.FlowNodeId = "OtherTask";
        using var client = context.CreateClient();

        using var response = await client.PostAsJsonAsync(route, result);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await context.AssertStillActiveAsync(task);
    }

    // Testzweck: Eine manipulierte UserId darf weder den Kompatibilitätswert im Ergebnis
    // noch den separat persistierten und über die API gelieferten Akteur ersetzen.
    [TestCase("/usertask")]
    [TestCase("/form/result")]
    public async Task Completion_ShouldPersistTheAuthenticatedActorOutsideFormData(string route)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("");
        var result = ResultFor(task);
        result.Data = new ExpandoObject();
        ((IDictionary<string, object?>)result.Data)["UserId"] = Guid.NewGuid().ToString();
        ((IDictionary<string, object?>)result.Data)["answer"] = "approved";
        using var client = context.CreateClient();

        using var response = await client.PostAsJsonAsync(route, result);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var instance = await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
        var token = instance.Tokens.Single(candidate => candidate.Id == task.Token.Id);
        var data = (IDictionary<string, object?>)token.OutputData!;
        data["UserId"]!.ToString().Should().Be(CompletionContext.UserId.ToString());
        data["answer"].Should().Be("approved");
        token.CompletedByUserId.Should().Be(CompletionContext.UserId);
        var payload = await client.GetFromJsonAsync<JsonElement>($"/instance/{instance.InstanceId}");
        payload.GetProperty("result").GetProperty("tokens").EnumerateArray()
            .Single(value => value.GetProperty("id").GetGuid() == task.Token.Id)
            .GetProperty("completedByUserId").GetGuid().Should().Be(CompletionContext.UserId);
    }

    // Testzweck: Ein erneuter Abschluss darf keinen zweiten Zustandsübergang auslösen.
    [TestCase("/usertask")]
    [TestCase("/form/result")]
    public async Task Completion_ShouldRejectAnAlreadyCompletedTask(string route)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("");
        using var client = context.CreateClient();
        using var first = await client.PostAsJsonAsync(route, ResultFor(task));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using var repeated = await client.PostAsJsonAsync(route, ResultFor(task));

        repeated.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Gleichzeitige Abschlüsse über beide Routen teilen dieselbe Sperre und
    // können gemeinsam nur einen Zustandsübergang und einen verifizierten Akteur erzeugen.
    [Test]
    public async Task Completion_ShouldSerializeCompetingRoutes()
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("");
        using var client = context.CreateClient();

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/usertask", ResultFor(task)),
            client.PostAsJsonAsync("/form/result", ResultFor(task)));
        try
        {
            responses.Select(response => response.StatusCode)
                .Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.NotFound]);
            var instance = await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
            instance.IsFinished.Should().BeTrue();
            instance.Tokens.Single(token => token.Id == task.Token.Id).CompletedByUserId.Should().Be(CompletionContext.UserId);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    // Testzweck: Eine veraltete Subscription darf weder ein Token einer anderen Instanz
    // noch einer anderen Definition legitimieren, auch nicht für den Operator.
    [TestCase("/usertask")]
    [TestCase("/form/result")]
    public async Task Completion_ShouldRejectAStaleDefinitionBinding(string route)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("");
        task.DefinitionId = Guid.NewGuid();
        await context.Storage.SubscriptionStorage.AddUserTaskSubscription(task);
        using var client = context.CreateClient(isOperator: true);

        using var response = await client.PostAsJsonAsync(route, ResultFor(task));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await context.AssertStillActiveAsync(task);
    }

    // Testzweck: Ein API-Aufruf darf sich ohne authentifizierte Identität keinen Abschluss
    // erschleichen; der ältere Formularpfad ist ebenso geschützt wie die Aufgabenroute.
    [TestCase("/usertask")]
    [TestCase("/form/result")]
    public async Task Completion_ShouldRejectAnAnonymousRequest(string route)
    {
        using var context = new CompletionContext();
        var task = await context.StartAsync("");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        using var response = await client.PostAsJsonAsync(route, ResultFor(task));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await context.AssertStillActiveAsync(task);
    }

    private static UserTaskResultDto ResultFor(UserTaskSubscription task) => new()
    {
        ProcessInstanceId = task.ProcessInstanceId,
        TokenId = task.Token.Id,
        FlowNodeId = "Review"
    };

    private sealed class CompletionContext : IDisposable
    {
        internal static readonly Guid UserId = Guid.Parse("bf68c19d-6a91-4086-a201-b4ea660bd957");
        private const string Issuer = "https://issuer.test/realms/flowzer";
        private const string Audience = "flowzer-api";
        // Ausschließlich synthetischer Signaturschlüssel für den lokalen Test-IdP.
        private static readonly SymmetricSecurityKey SigningKey =
            new(Encoding.UTF8.GetBytes("flowzer-test-only-signing-key-32-bytes-or-more"));
        private readonly string? _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flowzer-completion-test", Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<Program> _factory;

        internal CompletionContext()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            Storage = new Storage();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("TimerScheduler:Enabled", "false");
                builder.UseSetting("ServiceTaskWebhooks:Enabled", "false");
                builder.UseSetting("RateLimiting:Enabled", "false");
                builder.UseSetting("Authentication:Scheme", "JwtBearer");
                builder.UseSetting("Authentication:JwtBearer:Authority", Issuer);
                builder.UseSetting("Authentication:JwtBearer:Audience", Audience);
                builder.UseSetting("Authentication:JwtBearer:Roles:Operator", "operator");
                builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme, options =>
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                            new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } })));
            });
        }

        internal Storage Storage { get; }

        internal HttpClient CreateClient(bool isOperator = false)
        {
            var claims = new List<Claim>
            {
                new("sub", UserId.ToString()), new("preferred_username", "bert"),
                new("groups", "/team/review")
            };
            if (isOperator) claims.Add(new Claim("roles", "operator"));
            var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
                Subject = new ClaimsIdentity(claims)
            });
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            return client;
        }

        internal async Task<UserTaskSubscription> StartAsync(string assignment)
        {
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(), DefinitionId = "Definitions_Completion", Hash = "test",
                SavedByUser = UserId, SavedOn = DateTime.UtcNow, Version = new Model.Version(1, 0), IsActive = false
            };
            await Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                { DefinitionId = definition.DefinitionId, Name = "Review" });
            await Storage.DefinitionStorage.StoreDefinition(definition);
            await Storage.DefinitionStorage.StoreBinary(definition.Id, $$"""
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                    xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Completion" targetNamespace="test">
                  <bpmn:process id="Process_Completion" isExecutable="true">
                    <bpmn:startEvent id="Start"><bpmn:outgoing>ToReview</bpmn:outgoing></bpmn:startEvent>
                    <bpmn:sequenceFlow id="ToReview" sourceRef="Start" targetRef="Review" />
                    <bpmn:userTask id="Review" name="Review">
                      <bpmn:extensionElements>
                        <zeebe:formDefinition formKey="Approval" />
                        <zeebe:assignmentDefinition {{assignment}} />
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
            var instance = await engine.StartProcessInstance(definition.DefinitionId);
            return (await Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
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
}
