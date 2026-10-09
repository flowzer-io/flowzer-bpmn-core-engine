using System.Net;
using System.Net.Http.Json;
using BPMN.HumanInteraction;
using FilesystemStorageSystem;
using FlowzerDmn.Parsing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Originale Demo-Assets, echte HTTP-Akteure und ausschließlich isolierte synthetische Ablage.</summary>
internal sealed class TickyTaskVacationTestContext : IDisposable
{
    internal const string DefinitionKey = "tickytask-demo-urlaub";
    internal static readonly Guid SupervisorSubject = Guid.Parse("31000000-0000-4000-8000-000000000001");
    internal static readonly Guid SubstituteSubject = Guid.Parse("31000000-0000-4000-8000-000000000002");
    internal static readonly Guid PersonnelSubject = Guid.Parse("31000000-0000-4000-8000-000000000003");
    internal static readonly Guid OtherPersonnelSubject = Guid.Parse("31000000-0000-4000-8000-000000000004");
    private readonly AuthenticatedWorkflowTestContext _context = new();
    internal Storage Storage => _context.Storage;
    internal Guid InitiatorId { get; private set; }
    internal Guid SupervisorId { get; private set; }
    internal Guid SubstituteId { get; private set; }
    internal Guid PersonnelGroupId { get; private set; }
    internal Guid DefinitionId { get; private set; }
    internal Guid InstanceId { get; private set; }

    internal HttpClient Client(Guid? subject = null) => _context.CreateClient(userId: subject,
        authorizedClientId: "synthetic-tickytask-demo");

    internal async Task InitializeAsync()
    {
        // Ohne echtes FEEL gilt dieser Durchstich nicht als geprüft; kein stiller Skip.
        Convert.ToDouble(core_engine.FlowzerConfig.Default.FeelEngine.Evaluate("1 + 1", new Dictionary<string, object?>()))
            .Should().Be(2);
        await PublishDirectoryAsync();
        await FormTestSeed.StoreAsync(Storage, "TT Demo Urlaubsantrag", Asset("formulare/antrag.json"));
        await FormTestSeed.StoreAsync(Storage, "TT Demo Urlaubsentscheidung", Asset("formulare/entscheidung.json"));
        await FormTestSeed.StoreAsync(Storage, "TT Demo Urlaubskorrektur", Asset("formulare/korrektur.json"));
        var engine = _context.Services.GetRequiredService<BpmnBusinessLogic>();
        var dmn = Asset("vorgesetzter.dmn").Replace("__DEMO_SUPERVISOR_USER_ID__", SupervisorId.ToString("D"), StringComparison.Ordinal);
        var decisions = DmnModelParser.Parse(dmn);
        await engine.SaveDecisionVersion(decisions.Id, decisions.Name ?? decisions.Id, dmn,
            decisions.Decisions.Select(item => new DecisionSummary(item.Id, item.Name ?? item.Id)).ToArray(),
            AuthenticatedWorkflowTestContext.UserId);
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(), DefinitionId = DefinitionKey, Hash = "synthetic-example",
            SavedByUser = AuthenticatedWorkflowTestContext.UserId, SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0), IsActive = false
        };
        DefinitionId = definition.Id;
        await Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = DefinitionKey, Name = "TT Demo Urlaub" });
        await Storage.DefinitionStorage.StoreDefinition(definition);
        await Storage.DefinitionStorage.StoreBinary(definition.Id,
            Asset("urlaubsantrag.bpmn").Replace("__DEMO_PERSONNEL_GROUP_ID__", PersonnelGroupId.ToString("D"), StringComparison.Ordinal));
        await engine.DeployDefinition(definition);
    }

    internal Dictionary<string, object?> Application(Guid? substitute = null) => new()
    {
        ["von"] = "2026-10-12", ["bis"] = "2026-10-16", ["arbeitstage"] = 5,
        ["vertretung"] = new { kind = "user", id = (substitute ?? SubstituteId).ToString("D") },
        ["bemerkung"] = "Synthetischer Demo-Antrag"
    };

    internal async Task StartAsync()
    {
        using var owner = Client();
        using var response = await owner.PostAsJsonAsync($"/definition/meta/{DefinitionKey}/instance",
            new { expectedDefinitionId = DefinitionId, variables = Application() });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        InstanceId = (await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>())!.Result!.InstanceId;
    }

    internal async Task<UserTaskSubscription[]> TasksAsync() => (await Storage.SubscriptionStorage.GetAllUserTasks(InstanceId)).ToArray();
    internal async Task<ProcessInstanceInfo> InstanceAsync() => await Storage.InstanceStorage.GetProcessInstance(InstanceId);
    internal Guid? Actor(string node) => node switch
    {
        "Task_Supervisor" => SupervisorSubject, "Task_Substitute" => SubstituteSubject,
        "Task_Personnel" => PersonnelSubject, "Task_Correction" => null,
        _ => throw new ArgumentException("Unknown synthetic actor node.", nameof(node))
    };

    internal async Task ClaimAsync(HttpClient client, UserTaskSubscription task)
    {
        using var response = await client.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    internal async Task CompleteAsync(string node, string? action = "approve", Dictionary<string, object?>? data = null)
    {
        var task = (await TasksAsync()).Single(item => item.Token.CurrentFlowNode!.Id == node);
        using var client = Client(Actor(node));
        await ClaimAsync(client, task);
        using var response = await client.PostAsJsonAsync("/usertask", Bound(task, action, data ?? new() { ["begruendung"] = "Synthetische Prüfung" }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    internal object Bound(UserTaskSubscription task, string? action, Dictionary<string, object?> data) => new
    {
        processInstanceId = InstanceId, tokenId = task.Token.Id, flowNodeId = task.Token.CurrentFlowNode!.Id,
        expectedTaskRevision = 1, expectedUserTaskId = task.Id, expectedDefinitionId = task.DefinitionId,
        requireAssignedToCurrentUser = true, actionId = action, data
    };

    private async Task PublishDirectoryAsync()
    {
        var subjects = new[] { AuthenticatedWorkflowTestContext.UserId, SupervisorSubject, SubstituteSubject, PersonnelSubject, OtherPersonnelSubject };
        var rawGroupId = Guid.NewGuid();
        var rawUsers = subjects.Select(subject => new DirectoryUser
        {
            Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak, Issuer = AuthenticatedWorkflowTestContext.Issuer,
            Subject = subject.ToString(), DisplayName = "Synthetic " + subject, IsActive = true
        }).ToList();
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = AuthenticatedWorkflowTestContext.Issuer, CompletedAtUtc = DateTime.UtcNow,
            Users = rawUsers,
            Groups = [new DirectoryGroup { Id = rawGroupId, SourceKind = DirectorySourceKind.Keycloak,
                Issuer = AuthenticatedWorkflowTestContext.Issuer, ExternalId = "synthetic-personnel", Name = "Synthetic Personnel", Path = "/Synthetic/Personnel", IsActive = true }],
            Memberships = rawUsers.Where(user => user.Subject == PersonnelSubject.ToString() || user.Subject == OtherPersonnelSubject.ToString())
                .Select(user => new DirectoryMembership { UserId = user.Id, GroupId = rawGroupId }).ToList()
        };
        var directory = Storage.IdentityDirectoryStorage;
        (await directory.TryStartSync(snapshot.Issuer, snapshot.GenerationId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5))).Should().BeTrue();
        await directory.PublishSnapshot(snapshot);
        var published = (await directory.GetActiveSnapshot())!;
        InitiatorId = published.Users.Single(user => user.Subject == AuthenticatedWorkflowTestContext.UserId.ToString()).Id;
        SupervisorId = published.Users.Single(user => user.Subject == SupervisorSubject.ToString()).Id;
        SubstituteId = published.Users.Single(user => user.Subject == SubstituteSubject.ToString()).Id;
        PersonnelGroupId = published.Groups.Single().Id;
    }

    private static string Asset(string relativePath) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TickyTaskVacation", relativePath));
    public void Dispose() => _context.Dispose();
}
