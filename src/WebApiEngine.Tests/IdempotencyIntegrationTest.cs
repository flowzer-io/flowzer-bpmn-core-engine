using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Idempotency;
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

    // Testzweck: Fachlich gleiche JSON-Zahlen bleiben unabhängig von ihrer zulässigen
    // Dezimal-/Exponentschreibweise derselbe idempotente Request.
    [Test]
    public async Task Start_ShouldCanonicalizeEquivalentJsonNumbers()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", """{"components":[{"type":"number","key":"days"}]}""");
        await context.DeployAsync("", startFormKey: "Approval");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "equivalent-numbers");
        var path = "/definition/meta/Definitions_Completion/instance";

        using var first = await client.PostAsync(path, Json("""{"variables":{"days":1}}"""));
        using var decimalReplay = await client.PostAsync(path, Json("""{"variables":{"days":1.0}}"""));
        using var exponentReplay = await client.PostAsync(path, Json("""{"variables":{"days":1e0}}"""));

        first.EnsureSuccessStatusCode();
        decimalReplay.EnsureSuccessStatusCode();
        exponentReplay.EnsureSuccessStatusCode();
        var expected = InstanceId(await first.Content.ReadFromJsonAsync<JsonElement>());
        InstanceId(await decimalReplay.Content.ReadFromJsonAsync<JsonElement>()).Should().Be(expected);
        InstanceId(await exponentReplay.Content.ReadFromJsonAsync<JsonElement>()).Should().Be(expected);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Die Hash-Kanonisierung selbst behandelt mathematisch identische
    // JSON-Zahlentokens gleich, auch bevor ein konkretes HTTP-DTO sie deserialisiert.
    [Test]
    public void RequestHash_ShouldCanonicalizeEquivalentJsonNumberTokens()
    {
        var actor = new WebApiEngine.Auth.CurrentUserContext(Guid.NewGuid(), "test", false)
        {
            Identity = new Model.AuthenticatedSubject("https://issuer.test", "subject")
        };
        var hashes = new[] { "1", "1.0", "1e0", "10e-1" }.Select(rawNumber =>
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Request.Headers[HttpIdempotency.HeaderName] = "equivalent-numbers";
            using var payload = JsonDocument.Parse($"{{\"value\":{rawNumber}}}");
            return HttpIdempotency.Create(context.Request, actor, "test-operation", "test-resource", payload.RootElement)!.RequestHash;
        });

        hashes.Distinct().Should().ContainSingle();
    }

    // Testzweck: Scheitert bei der nichttransaktionalen Dateiablage erst das Festschreiben
    // des Idempotenz-Ergebnisses, bleibt die Reservierung als unklar erhalten; ein Retry
    // darf die bereits gespeicherte Instanz nicht duplizieren.
    [Test]
    public async Task Start_ShouldKeepReservationWhenCompletionFailsAfterFileMutation()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("");
        var request = new IdempotencyRequest(new string('a', 64), new string('b', 64), "workflow-start");
        var failingEngine = new BpmnBusinessLogic(new FailingIdempotencyCompleteStorageProvider(
            new FileSystemTransactionalStorageProvider()));

        Func<Task> first = async () => await failingEngine.StartProcessInstance(
            definition.DefinitionId, idempotency: request);
        await first.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Injected idempotency completion failure.");

        Func<Task> retry = async () => await new BpmnBusinessLogic(new FileSystemTransactionalStorageProvider())
            .StartProcessInstance(definition.DefinitionId, idempotency: request);
        await retry.Should().ThrowAsync<IdempotencyConflictException>()
            .WithMessage("*still in progress*");
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Auch beim Aufgabenabschluss bleibt ein nach der Fachmutation unklarer
    // Datei-Request gesperrt, statt beim Retry als unbekannte Aufgabe zu erscheinen.
    [Test]
    public async Task Completion_ShouldKeepReservationWhenResultPersistenceFailsAfterFileMutation()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        var request = new IdempotencyRequest(new string('c', 64), new string('d', 64), "user-task-completion");
        var actor = new WebApiEngine.Auth.CurrentUserContext(AuthenticatedWorkflowTestContext.UserId, "test", false)
        {
            Identity = new Model.AuthenticatedSubject(AuthenticatedWorkflowTestContext.Issuer, "subject"),
            Names = ["bert"], Groups = ["/team/review"]
        };
        var result = new Model.UserTaskResult
        {
            ProcessInstanceId = task.ProcessInstanceId, TokenId = task.Token.Id,
            FlowNodeId = task.Token.CurrentFlowNode!.Id
        };

        Func<Task> first = async () => await new BpmnBusinessLogic(new FailingIdempotencyCompleteStorageProvider(
                new FileSystemTransactionalStorageProvider()))
            .CompleteUserTaskAsync(result, actor, idempotency: request);
        await first.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Injected idempotency completion failure.");

        Func<Task> retry = async () => await new BpmnBusinessLogic(new FileSystemTransactionalStorageProvider())
            .CompleteUserTaskAsync(result, actor, idempotency: request);
        await retry.Should().ThrowAsync<IdempotencyConflictException>()
            .WithMessage("*still in progress*");
        (await context.Storage.SubscriptionStorage.GetAllUserTasks(task.ProcessInstanceId!.Value)).Should().BeEmpty();
    }

    // Testzweck: Die normale Aufbewahrungsbereinigung entfernt nur abgeschlossene
    // Ergebnisse; unklare Reservierungen bleiben bis zu einer bewussten Klärung gesperrt.
    [Test]
    public async Task Retention_ShouldKeepIncompleteReservationAndDeleteCompletedResult()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var expired = DateTime.UtcNow.AddDays(-1);
        var incomplete = Record('e', expired);
        var completed = Record('f', expired);
        (await context.Storage.IdempotencyStorage.TryCreate(incomplete)).Should().BeTrue();
        (await context.Storage.IdempotencyStorage.TryCreate(completed)).Should().BeTrue();
        await context.Storage.IdempotencyStorage.Complete(completed.ScopeHash, Guid.NewGuid());

        await context.Storage.IdempotencyStorage.DeleteExpired(DateTime.UtcNow);

        (await context.Storage.IdempotencyStorage.Get(incomplete.ScopeHash)).Should().NotBeNull();
        (await context.Storage.IdempotencyStorage.Get(completed.ScopeHash)).Should().BeNull();
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

    // Testzweck: Die gewählte fachliche Aktion gehört zum idempotenten Request-Inhalt.
    // Derselbe Schlüssel darf nicht nachträglich für eine andere Entscheidung gelten.
    [Test]
    public async Task Completion_ShouldConflictForChangedActionAfterSuccess()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", """
            {"flowzer":{"contractVersion":4,"actions":[
              {"id":"approve","label":"Freigeben","variant":"primary","set":[{"field":"decision","value":"approved"}]},
              {"id":"reject","label":"Ablehnen","variant":"danger","set":[{"field":"decision","value":"rejected"}]}
             ]},"components":[{"type":"hidden","key":"decision","validate":{"required":true}}]}
            """);
        var task = await context.StartAsync("assignee=\"bert\"");
        using var client = context.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "completion-action-conflict");
        var first = Result(task, "ignored");
        first.Data = new System.Dynamic.ExpandoObject();
        first.ActionId = "approve";
        (await client.PostAsJsonAsync("/usertask", first)).EnsureSuccessStatusCode();

        var changed = Result(task, "ignored");
        changed.Data = new System.Dynamic.ExpandoObject();
        changed.ActionId = "reject";
        using var conflict = await client.PostAsJsonAsync("/form/result", changed);

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
    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
    private static IdempotencyRecord Record(char scope, DateTime expiresAt) => new()
    {
        ScopeHash = new string(scope, 64), RequestHash = new string('0', 64), Operation = "test",
        CreatedAt = expiresAt.AddDays(-1), ExpiresAt = expiresAt
    };
    private static UserTaskResultDto Result(Model.UserTaskSubscription task, string answer) => new()
    {
        ProcessInstanceId = task.ProcessInstanceId, TokenId = task.Token.Id,
        FlowNodeId = task.Token.CurrentFlowNode!.Id,
        Data = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Dynamic.ExpandoObject>($"{{\"answer\":\"{answer}\"}}")
    };

}
