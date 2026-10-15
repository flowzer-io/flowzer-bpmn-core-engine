using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using BPMN.HumanInteraction;
using FluentAssertions;
using StorageSystem;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.DependencyInjection;
using WebApiEngine.BusinessLogic;
using FilesystemStorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Wirkliche Engine-/HTTP-Zuweisung mit ausschließlich synthetischem Directory und isolierter Dateiablage.</summary>
[NonParallelizable]
public sealed class DynamicDirectoryTaskAssignmentTest
{
    private static readonly Guid InitiatorId = Guid.Parse("11000000-0000-4000-8000-000000000001");
    private static readonly Guid SubstituteId = Guid.Parse("11000000-0000-4000-8000-000000000002");
    private static readonly Guid SubstituteSubject = Guid.Parse("11000000-0000-4000-8000-000000000009");
    private static readonly Guid GroupId = Guid.Parse("22000000-0000-4000-8000-000000000001");

    // Testzweck: Der als einzelner Formularwert gewählte Benutzer wird vor Veröffentlichung
    // der Aufgabe als stabile Directory-ID festgehalten; Text und Gruppen sind kein Ersatz.
    [Test]
    public async Task VariableSource_ShouldAssignTheSelectedStableUser()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectory(context);
        var task = await context.StartAsync("", Values(Reference(directory.Substitute)), assignmentExtensionXml: Assignment("variable:vertretung"), taskFormKey: null);
        task.AssignmentMode.Should().Be(UserTaskAssignmentMode.Directory);
        task.DirectoryAssigneeUserId.Should().Be(directory.Substitute);
        task.Assignee.Should().BeNull();
        task.DirectoryCandidateUserIds.Should().BeEmpty();
        task.DirectoryCandidateGroupIds.Should().BeEmpty();
    }

    // Testzweck: Ein explizit gemappter Task-Eingang hat Vorrang vor dem gleichnamigen
    // Root-Wert; dadurch bleiben fachliche Modellbindungen statt frei gesuchter Pfade maßgeblich.
    [Test]
    public async Task VariableSource_ShouldPreferExplicitTaskInputMapping()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectory(context);
        var values = Values(Reference(directory.Substitute));
        ((IDictionary<string, object?>)values)["actualUser"] = Reference(directory.Initiator);
        var extension = Assignment("variable:vertretung")
            + "<zeebe:ioMapping><zeebe:input source='=actualUser' target='vertretung' /></zeebe:ioMapping>";
        var task = await context.StartAsync("", values, assignmentExtensionXml: extension, taskFormKey: null);
        task.DirectoryAssigneeUserId.Should().Be(directory.Initiator);
    }

    // Testzweck: Die initiatorgebundene Korrekturaufgabe verwendet ausschließlich echte
    // Startmetadaten. Gleichnamige/gefälschte Formularvariablen bestimmen nie den Akteur.
    [Test]
    public async Task InitiatorSource_ShouldUseAuthenticatedStartMetadataNotFormValues()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectory(context);
        var definition = await context.DeployAsync("", assignmentExtensionXml: Assignment("initiator"), taskFormKey: null);
        using var client = context.CreateClient();
        using var response = await client.PostAsJsonAsync($"/definition/meta/{definition.DefinitionId}/instance", new
        {
            variables = new { initiator = Reference(directory.Substitute), UserId = directory.Substitute, vertretung = Reference(directory.Substitute) }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var instance = (await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>())!.Result!;
        var task = (await context.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
        task.DirectoryAssigneeUserId.Should().Be(directory.Initiator);
    }

    // Testzweck: Fehlende/malforme, Gruppen-, inaktive oder mehrdeutige Zielwerte erzeugen
    // keine öffentlich bearbeitbare neue Aufgabe und verraten keine eingereichten Rohwerte.
    [TestCase("missing")]
    [TestCase("plain-id")]
    [TestCase("group")]
    [TestCase("unknown")]
    [TestCase("inactive")]
    [TestCase("extra-property")]
    [TestCase("foreign-issuer")]
    [TestCase("duplicate-id")]
    [TestCase("duplicate-identity")]
    public async Task VariableSource_ShouldRejectInvalidOrUnavailableUsers(string scenario)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectory(context, scenario);
        object? value = scenario switch
        {
            "plain-id" => directory.Substitute.ToString(),
            "group" => new { kind = "group", id = directory.Group.ToString() },
            "unknown" => Reference(Guid.NewGuid()),
            "extra-property" => new { kind = "user", id = directory.Substitute.ToString(), displayName = "untrusted" },
            _ => Reference(directory.Substitute)
        };
        var values = scenario == "missing" ? new ExpandoObject() : Values(value);
        var action = () => context.StartAsync("", values, assignmentExtensionXml: Assignment("variable:vertretung"), taskFormKey: null);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*directory assignment*");
        (await context.Storage.SubscriptionStorage.GetAllUserTasksExtended(Guid.Empty)).Should().BeEmpty();
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
    }

    // Testzweck: Spätere Variablenänderung und vollständiger Storage-/Engine-Roundtrip
    // dürfen eine bestehende Task-ID nicht an eine andere Person umhängen.
    [Test]
    public async Task VariableSource_ShouldRemainFrozenAcrossCorrectionAndReload()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectory(context);
        var original = await context.StartAsync("", Values(Reference(directory.Substitute)), assignmentExtensionXml: Assignment("variable:vertretung"), taskFormKey: null);
        using var client = context.CreateClient(isOperator: true);
        using var response = await client.PostAsJsonAsync($"/instance/{original.ProcessInstanceId}/modification", new InstanceModificationRequestDto
        {
            Variables = new InstanceModificationVariablesDto { Set = Values(Reference(directory.Initiator)) }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = (await new Storage().SubscriptionStorage.GetAllUserTasks(original.ProcessInstanceId!.Value)).Single();
        stored.Id.Should().Be(original.Id);
        stored.DirectoryAssigneeUserId.Should().Be(directory.Substitute);
        ((UserTask)stored.Token.CurrentFlowNode!).FlowzerDirectoryAssigneeSource.Should().Be("variable:vertretung");
        var engine = new BpmnBusinessLogic(new FileSystemTransactionalStorageProvider());
        var correction = new core_engine.InstanceModificationRequest(VariablesToSet: new Dictionary<string, object?> { ["vertretung"] = Reference(directory.Substitute) });
        var outcome = await engine.ModifyInstance(original.ProcessInstanceId.Value, correction, AuthenticatedWorkflowTestContext.UserId);
        outcome.Modified.Should().BeTrue();
        (await new Storage().SubscriptionStorage.GetAllUserTasks(original.ProcessInstanceId.Value)).Single().DirectoryAssigneeUserId.Should().Be(directory.Substitute);
    }

    // Testzweck: Der eingefrorene Bearbeiter ist kein Dauerrecht: ein neuer Directory-Stand
    // sperrt Anzeige/Arbeit sofort, obwohl JWT und Aufgaben-ID weiterhin vorhanden sind.
    [Test]
    public async Task VariableSource_ShouldEnforceRevocationWithoutReassigningTheTask()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectory(context);
        var task = await context.StartAsync("", Values(Reference(directory.Substitute)), assignmentExtensionXml: Assignment("variable:vertretung"), taskFormKey: null);
        using var substitute = context.CreateClient(userId: SubstituteSubject);
        using var before = await substitute.GetAsync($"/usertask/{task.Id}/form");
        before.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await PublishDirectory(context, "inactive");
        using var after = await substitute.GetAsync($"/usertask/{task.Id}/form");
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await context.Storage.SubscriptionStorage.GetAllUserTasks(task.ProcessInstanceId!.Value)).Single().DirectoryAssigneeUserId.Should().Be(directory.Substitute);
    }

    // Testzweck: Ein interner Start ohne verifizierte Metadaten darf die Initiatorquelle
    // niemals aus einer gleichnamigen Browser-/Prozessvariablen rekonstruieren.
    [Test]
    public async Task InitiatorSource_ShouldRejectStartWithoutVerifiedIdentity()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var directory = await PublishDirectory(context);
        var values = Values(Reference(directory.Substitute));
        ((IDictionary<string, object?>)values)["initiator"] = Reference(directory.Initiator);
        var action = () => context.StartAsync("", values, assignmentExtensionXml: Assignment("initiator"), taskFormKey: null);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*directory assignment*");
        (await context.Storage.SubscriptionStorage.GetAllUserTasksExtended(Guid.Empty)).Should().BeEmpty();
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
    }

    private static string Assignment(string source) => $"<flowzer:taskAssignment mode=\"directory\" assigneeSource=\"{source}\" />";
    private static object Reference(Guid id) => new { kind = "user", id = id.ToString() };
    private static ExpandoObject Values(object? value)
    {
        var values = new ExpandoObject(); ((IDictionary<string, object?>)values)["vertretung"] = value; return values;
    }

    private static async Task<(Guid Initiator, Guid Substitute, Guid Group)> PublishDirectory(AuthenticatedWorkflowTestContext context, string? scenario = null)
    {
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = AuthenticatedWorkflowTestContext.Issuer, CompletedAtUtc = DateTime.UtcNow,
            Users =
            [
                new DirectoryUser { Id = InitiatorId, SourceKind = DirectorySourceKind.Keycloak, Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = AuthenticatedWorkflowTestContext.UserId.ToString(), DisplayName = "Synthetic initiator", IsActive = true },
                new DirectoryUser { Id = SubstituteId, SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = SubstituteSubject.ToString(), DisplayName = "Synthetic substitute", IsActive = scenario != "inactive" }
            ],
            Groups = [new DirectoryGroup { Id = GroupId, SourceKind = DirectorySourceKind.Keycloak,
                Issuer = AuthenticatedWorkflowTestContext.Issuer, ExternalId = "synthetic-personal", Name = "Synthetic", Path = "/Synthetic", IsActive = true }]
        };
        var directory = context.Storage.IdentityDirectoryStorage;
        (await directory.TryStartSync(snapshot.Issuer, snapshot.GenerationId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5))).Should().BeTrue();
        await directory.PublishSnapshot(snapshot);
        var published = (await directory.GetActiveSnapshot())!;
        var initiator = published.Users.Single(user => user.Subject == AuthenticatedWorkflowTestContext.UserId.ToString()).Id;
        var substitute = published.Users.Single(user => user.Subject == SubstituteSubject.ToString()).Id;
        var group = published.Groups.Single().Id;
        if (scenario is "foreign-issuer" or "duplicate-id" or "duplicate-identity")
        {
            // Nur diese isolierte synthetische Ablage wird absichtlich beschädigt. Der
            // normale Publisher verhindert beides; die Laufzeit muss dennoch fail-closed sein.
            var path = Path.Combine(context.Storage.GetBasePath("FileStorage/IdentityDirectory"), "identity-directory.json");
            var state = JObject.Parse(await File.ReadAllTextAsync(path));
            var users = (JArray)state["ActiveSnapshot"]!["Users"]!;
            var target = users.Single(user => user["Subject"]!.Value<string>() == SubstituteSubject.ToString());
            if (scenario == "foreign-issuer") target["Issuer"] = "https://foreign.test";
            else
            {
                var duplicate = target.DeepClone();
                if (scenario == "duplicate-id") duplicate["Subject"] = "other-subject";
                else duplicate["Id"] = Guid.NewGuid().ToString();
                users.Add(duplicate);
            }
            await File.WriteAllTextAsync(path, state.ToString());
        }
        return (initiator, substitute, group);
    }
}
