using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Öffentlicher HTTP-Vertrag mit echter Engine und isolierter Dateiablage.</summary>
[NonParallelizable]
public class IdempotencyIntegrationTest
{
    // Testzweck: Ein identisch wiederholter Start liefert dieselbe Instanz und legt
    // weder vor noch nach einem neuen BusinessLogic-Objekt einen zweiten Vorgang an.
    [Test]
    public async Task Start_ShouldReplayPersistedResultForSameActorAndContent()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", """{"components":[{"type":"textfield","key":"reason"},{"type":"textfield","key":"alpha"}]}""");
        await context.DeployAsync("", startFormKey: "Approval");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "start-one");
        var path = "/definition/meta/Definitions_Completion/instance";

        var first = await client.PostAsJsonAsync(path, new { variables = new { reason = "Holiday", alpha = (string?)null } });
        var second = await client.PostAsJsonAsync(path, new { variables = new { alpha = (string?)null, reason = "Holiday" } });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        InstanceId(await first.Content.ReadFromJsonAsync<JsonElement>()).Should()
            .Be(InstanceId(await second.Content.ReadFromJsonAsync<JsonElement>()));
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
        var idempotencyPath = Path.Combine(Environment.GetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName)!, "FileStorage", "Idempotency");
        var stored = await File.ReadAllTextAsync(Directory.GetFiles(idempotencyPath, "*.json").Single());
        stored.Should().NotContain("start-one").And.NotContain("Holiday").And.NotContain(AuthenticatedWorkflowTestContext.Issuer);
    }

    // Testzweck: Derselbe Schlüssel mit anderem fachlichem Inhalt ist ein 409 und
    // darf keinen zweiten Start erzeugen oder Eingabewerte im Fehler spiegeln.
    [Test]
    public async Task Start_ShouldConflictForChangedContent()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", """{"components":[{"type":"textfield","key":"reason"}]}""");
        await context.DeployAsync("", startFormKey: "Approval");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "start-conflict");
        var path = "/definition/meta/Definitions_Completion/instance";
        (await client.PostAsJsonAsync(path, new { variables = new { reason = "ORIGINAL_PRIVATE" } })).EnsureSuccessStatusCode();

        using var conflict = await client.PostAsJsonAsync(path, new { variables = new { reason = "CHANGED_PRIVATE" } });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        conflict.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await conflict.Content.ReadAsStringAsync()).Should().NotContain("ORIGINAL_PRIVATE").And.NotContain("CHANGED_PRIVATE");
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Schlüssel sind an die verifizierte Person gebunden; zwei Personen
    // dürfen denselben Clientschlüssel verwenden, ohne Ergebnisse zu teilen.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Start_ShouldScopeKeyByAuthenticatedSubjectAndIssuer(bool differentIssuer)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.DeployAsync("");
        var path = "/definition/meta/Definitions_Completion/instance";
        var firstUser = Guid.Parse("00000000-0000-0000-0000-000000000011");
        using var first = context.CreateClient(userId: firstUser);
        using var second = context.CreateClient(
            userId: differentIssuer ? firstUser : Guid.Parse("00000000-0000-0000-0000-000000000022"),
            issuer: differentIssuer ? AuthenticatedWorkflowTestContext.Issuer + "-second" : null);
        first.DefaultRequestHeaders.Add("Idempotency-Key", "shared-client-key");
        second.DefaultRequestHeaders.Add("Idempotency-Key", "shared-client-key");

        (await first.PostAsJsonAsync(path, new { })).EnsureSuccessStatusCode();
        (await second.PostAsJsonAsync(path, new { })).EnsureSuccessStatusCode();
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().HaveCount(2);
    }

    // Testzweck: Beide historischen Abschlussrouten teilen denselben Datensatz; nach
    // erfolgreichem Abschluss wird die identische Wiederholung nicht als 404 behandelt.
    [Test]
    public async Task Completion_ShouldReplayAcrossBothRoutes()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "complete-once");
        var result = Result(task, "yes");

        (await client.PostAsJsonAsync("/usertask", result)).EnsureSuccessStatusCode();
        using var replay = await client.PostAsJsonAsync("/form/result", result);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await context.Storage.SubscriptionStorage.GetAllUserTasks(task.ProcessInstanceId!.Value)).Should().BeEmpty();
    }

    // Testzweck: Eine abweichende Wiederholung wird auch nach entferntem Task als
    // Konflikt erkannt, nicht erneut ausgeführt oder fälschlich als unbekannt behandelt.
    [Test]
    public async Task Completion_ShouldConflictForChangedContentAfterSuccess()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", """{"components":[{"type":"textfield","key":"answer"}]}""");
        var task = await context.StartAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "completion-conflict");
        (await client.PostAsJsonAsync("/usertask", Result(task, "yes"))).EnsureSuccessStatusCode();

        using var conflict = await client.PostAsJsonAsync("/form/result", Result(task, "no"));
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // Testzweck: Fehlende, leere, mehrfache oder übergroße Header öffnen weder einen
    // unbeschränkten Speicherpfad noch erzeugen sie einen Vorgang.
    [TestCase("contains space")]
    [TestCase("multiple")]
    [TestCase("oversized")]
    public async Task InvalidKey_ShouldBeBadRequestWithoutMutation(string kind)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.DeployAsync("");
        using var client = context.CreateClient();
        if (kind == "oversized") client.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", new string('x', 201));
        else if (kind == "multiple") client.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", ["one", "two"]);
        else client.DefaultRequestHeaders.TryAddWithoutValidation("Idempotency-Key", kind);
        using var response = await client.PostAsJsonAsync("/definition/meta/Definitions_Completion/instance", new { });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
    }

    private static Guid InstanceId(JsonElement response) => response.GetProperty("result").GetProperty("instanceId").GetGuid();
    private static UserTaskResultDto Result(Model.UserTaskSubscription task, string answer) => new()
    {
        ProcessInstanceId = task.ProcessInstanceId, TokenId = task.Token.Id,
        FlowNodeId = task.Token.CurrentFlowNode!.Id,
        Data = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Dynamic.ExpandoObject>($"{{\"answer\":\"{answer}\"}}")
    };
}
