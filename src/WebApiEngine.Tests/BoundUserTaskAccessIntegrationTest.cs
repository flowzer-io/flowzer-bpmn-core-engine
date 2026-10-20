using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>Auch Nicht-Abschlussaktionen müssen persönliche Übernahme und ursprüngliche Vorgangsbindung im Aufgabenlock prüfen.</summary>
[NonParallelizable]
public sealed class BoundUserTaskAccessIntegrationTest
{
    private static readonly Dictionary<string, string> Settings = new()
    {
        ["FormEmbedding:Enabled"] = "true", ["FormEmbedding:PublicOrigin"] = "https://flowzer.test",
        ["FormEmbedding:AllowedHostOrigins:0"] = "https://host.test"
    };

    // Testzweck: Eine echte Migration behält Task-ID und Claimrevision; kein V1-Auftrag darf danach V2 lesen oder verändern.
    [TestCase("claim")][TestCase("release")][TestCase("draft")][TestCase("save")]
    // Testzweck: Dieselbe Migrationsbindung gilt auch für Link und beide Directorywege.
    [TestCase("link")][TestCase("search")][TestCase("resolve")]
    public async Task MigratedTask_ShouldRejectEveryOldBindingBeforeDataOrEffect(string operation)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        var task = await Start(context); using var person = context.CreateClient();
        if (operation != "claim") await Claim(person, task);
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        var target = await context.Storage.DefinitionStorage.GetDefinitionById(task.DefinitionId);
        var xml = await context.Storage.DefinitionStorage.GetBinary(task.DefinitionId);
        target.Id = Guid.NewGuid(); target.Version = new Model.Version(2, 0); target.IsActive = false;
        await context.Storage.DefinitionStorage.StoreDefinition(target);
        await context.Storage.DefinitionStorage.StoreBinary(target.Id, xml);
        await engine.DeployDefinition(target);
        (await engine.MigrateInstances([task.ProcessInstanceId!.Value], target.Id, AuthenticatedWorkflowTestContext.UserId))
            .Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();
        var current = (await context.Storage.SubscriptionStorage.GetAllUserTasks(task.ProcessInstanceId.Value)).Single();
        current.Id.Should().Be(task.Id); current.DefinitionId.Should().Be(target.Id);
        using var rejected = await Send(person, task, operation);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("user_task.binding_conflict");
        (await rejected.Content.ReadAsStringAsync()).Should().NotContain(target.Id.ToString());
        await context.AssertStillActiveAsync(current);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().HaveCount(operation == "claim" ? 0 : 1);
    }

    // Testzweck: Persönlicher Vorab-GET verliert nach Freigabe seine Gültigkeit; Kandidaten- oder Operatorrechte ersetzen keinen Claim.
    [TestCase("draft", false)][TestCase("save", false)][TestCase("link", false)][TestCase("search", false)][TestCase("resolve", false)]
    // Testzweck: Operator ist ebenfalls kein Ersatz für die gerade verlorene persönliche Übernahme.
    [TestCase("draft", true)][TestCase("save", true)][TestCase("link", true)][TestCase("search", true)][TestCase("resolve", true)]
    public async Task ReleasedTask_ShouldNotAllowPersonalDataOrLinkAsCandidate(string operation, bool isOperator)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        var task = await Start(context); using var person = context.CreateClient(isOperator: isOperator);
        await Claim(person, task);
        (await person.GetAsync($"/usertask/{task.Id}")).EnsureSuccessStatusCode();
        (await person.PostAsJsonAsync($"/usertask/{task.Id}/release", new { expectedRevision = 1, reason = "Freigegeben" })).EnsureSuccessStatusCode();
        using var rejected = await Send(person, task, operation, revision: 2);
        rejected.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Select(row => row.Action).Should().Equal("claim", "release");
        await context.AssertStillActiveAsync(task);
    }

    // Testzweck: Tatsächlich persönliche aktuelle Aufträge funktionieren weiter; unvollständiges Speichern ist kein Abschluss.
    [TestCase("draft")][TestCase("save")][TestCase("link")][TestCase("search")][TestCase("resolve")][TestCase("release")]
    public async Task CurrentPersonalBinding_ShouldKeepWorkingWithoutAnotherLogin(string operation)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings); var task = await Start(context);
        using var person = context.CreateClient(); await Claim(person, task);
        using var response = await Send(person, task, operation); response.StatusCode.Should().Be(HttpStatusCode.OK);
        await context.AssertStillActiveAsync(task);
    }

    internal static string Binding(UserTaskSubscription task, bool personal = true) =>
        $"expectedProcessInstanceId={task.ProcessInstanceId}&expectedDefinitionId={task.DefinitionId}&requireAssignedToCurrentUser={personal.ToString().ToLowerInvariant()}";

    private static Task<HttpResponseMessage> Send(HttpClient client, UserTaskSubscription task, string operation, long revision = 1)
    {
        var query = Binding(task, personal: operation != "claim"); var path = $"/usertask/{task.Id}";
        return operation switch
        {
            "claim" => client.PostAsJsonAsync(path + "/claim?" + query, new { expectedRevision = 0 }),
            "release" => client.PostAsJsonAsync(path + "/release?" + query, new { expectedRevision = revision, reason = "Freigeben" }),
            "draft" => client.GetAsync(path + "/draft?" + query),
            "save" => client.PutAsJsonAsync(path + "/draft?" + query, new { expectedRevision = 0, expectedTaskRevision = revision, data = new { answer = "" } }),
            "link" => client.PostAsJsonAsync(path + "/form-link?" + query, new { hostOrigin = "https://host.test" }),
            "search" => client.GetAsync($"/identity-directory/user-tasks/{task.Id}/fields/representative/subjects?query=bert&kind=user&" + query),
            "resolve" => client.PostAsJsonAsync($"/identity-directory/user-tasks/{task.Id}/fields/representative/subjects/resolve?" + query,
                new { subjects = new[] { new { kind = "user", id = Guid.NewGuid() } } }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task Claim(HttpClient client, UserTaskSubscription task) =>
        (await client.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 })).EnsureSuccessStatusCode();

    private static async Task<string> Assignment(AuthenticatedWorkflowTestContext context) =>
        $"<flowzer:taskAssignment mode=\"directory\" candidateUserIds=\"{(await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!.Users.Single().Id}\" />";

    private static async Task<UserTaskSubscription> Start(AuthenticatedWorkflowTestContext context)
    {
        var now = DateTime.UtcNow;
        var snapshot = new DirectorySnapshot { GenerationId = Guid.NewGuid(), Issuer = AuthenticatedWorkflowTestContext.Issuer,
            CompletedAtUtc = now, Users = [new DirectoryUser { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
                Issuer = AuthenticatedWorkflowTestContext.Issuer, Subject = AuthenticatedWorkflowTestContext.UserId.ToString(), DisplayName = "Bert", IsActive = true }] };
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(snapshot.Issuer, snapshot.GenerationId, now.AddSeconds(-1), now.AddMinutes(5))).Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
        await FormTestSeed.StoreAsync(context.Storage, "Bound", """
            {"flowzer":{"contractVersion":2},"components":[
              {"type":"textfield","key":"answer","validate":{"required":true}},
              {"type":"flowzerSubject","key":"representative","flowzer":{"subjectSelection":{"allowUsers":true,"allowGroups":false}}}]}
            """);
        return await context.StartAsync("", assignmentExtensionXml: await Assignment(context), taskFormKey: "Bound");
    }
}
