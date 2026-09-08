using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Model;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>HTTP-Vertrag fuer private, revisionsgeschuetzte Aufgabenentwuerfe.</summary>
[NonParallelizable]
public sealed class UserTaskDraftIntegrationTest
{
    private const string DraftForm = """
        {
          "components": [
            { "type": "textfield", "key": "answer", "validate": { "required": true } },
            { "type": "textfield", "key": "employee", "disabled": true }
          ]
        }
        """;

    // Testzweck: Ein unvollstaendiger Entwurf darf angelegt, wieder geladen, mit der
    // aktuellen Revision ersetzt und anschliessend bewusst verworfen werden.
    [Test]
    public async Task Draft_ShouldRoundTripIncompleteDataAndRequireCurrentRevision()
    {
        using var context = await CreateContext();
        var task = await context.StartAsync("");
        using var client = context.CreateClient();

        var empty = await ReadDraft(client, task.Id);
        empty.GetProperty("revision").GetInt64().Should().Be(0);
        empty.GetProperty("data").EnumerateObject().Should().BeEmpty();

        using var createdResponse = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 0,
            data = new { answer = "" }
        });
        createdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = Result(await createdResponse.Content.ReadFromJsonAsync<JsonElement>());
        created.GetProperty("revision").GetInt64().Should().Be(1);
        created.GetProperty("data").GetProperty("answer").GetString().Should().BeEmpty();
        created.GetProperty("updatedAtUtc").GetDateTimeOffset().Offset.Should().Be(TimeSpan.Zero);

        using var updatedResponse = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 1,
            data = new { answer = "fast fertig" }
        });
        updatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loaded = await ReadDraft(client, task.Id);
        loaded.GetProperty("revision").GetInt64().Should().Be(2);
        loaded.GetProperty("data").GetProperty("answer").GetString().Should().Be("fast fertig");

        using var deleted = await client.DeleteAsync($"/usertask/{task.Id}/draft?expectedRevision=2");
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadDraft(client, task.Id)).GetProperty("revision").GetInt64().Should().Be(0);
    }

    // Testzweck: Zwei Tabs mit derselben Ausgangsrevision duerfen nicht still einander
    // ueberschreiben; genau der erste Write gewinnt und der Konflikt nennt nur die Revision.
    [Test]
    public async Task Draft_ShouldRejectAStaleRevisionWithoutOverwritingTheWinner()
    {
        using var context = await CreateContext();
        var task = await context.StartAsync("");
        using var client = context.CreateClient();

        using var first = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 0,
            data = new { answer = "erster Tab" }
        });
        using var stale = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 0,
            data = new { answer = "zweiter Tab" }
        });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        stale.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("task_draft.revision_conflict");
        problem.GetProperty("currentRevision").GetInt64().Should().Be(1);
        (await ReadDraft(client, task.Id)).GetProperty("data").GetProperty("answer")
            .GetString().Should().Be("erster Tab");
    }

    // Testzweck: Entwuerfe duerfen nur deklarierte beschreibbare Felder enthalten; fehlende
    // Pflichtwerte bleiben dagegen erlaubt, weil erst der Abschluss fachlich validiert.
    [Test]
    public async Task Draft_ShouldRejectUndeclaredAndReadOnlyFieldsButAllowMissingRequiredValues()
    {
        using var context = await CreateContext();
        var variables = new ExpandoObject();
        ((IDictionary<string, object?>)variables)["employee"] = "Serverwert";
        var task = await context.StartAsync("", variables);
        using var client = context.CreateClient();

        using var partial = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 0,
            data = new { }
        });
        using var unknown = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 1,
            data = new { answer = "ok", injected = "nein" }
        });
        using var readOnly = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 1,
            data = new { answer = "ok", employee = "Manipuliert" }
        });

        partial.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        readOnly.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var unknownProblem = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        unknownProblem.GetProperty("errors").GetProperty("injected")[0].GetString()
            .Should().Be("field.undeclared");
        var readOnlyProblem = await readOnly.Content.ReadFromJsonAsync<JsonElement>();
        readOnlyProblem.GetProperty("errors").GetProperty("employee")[0].GetString()
            .Should().Be("field.read_only");
    }

    // Testzweck: Eigentum kommt ausschliesslich aus dem authentifizierten Kontext. Eine
    // fremde Aufgabe bleibt verborgen; ein Operator erhaelt nur seinen eigenen leeren Entwurf.
    [Test]
    public async Task Draft_ShouldApplyTaskRightsAndKeepOwnersPrivate()
    {
        using var context = await CreateContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        using var owner = context.CreateClient(username: "bert");
        using var forbidden = context.CreateClient(userId: Guid.NewGuid(), username: "anna");
        using var operation = context.CreateClient(isOperator: true, userId: Guid.NewGuid(), username: "operator");

        using var saved = await owner.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 0,
            ownerUserId = Guid.NewGuid(),
            data = new { answer = "privat" }
        });
        using var hidden = await forbidden.GetAsync($"/usertask/{task.Id}/draft");
        var operatorDraft = await ReadDraft(operation, task.Id);

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        operatorDraft.GetProperty("revision").GetInt64().Should().Be(0);
        operatorDraft.GetProperty("data").EnumerateObject().Should().BeEmpty();
    }

    // Testzweck: Uebergrosse Entwuerfe werden vor Persistenz mit einem stabilen 413-Vertrag
    // abgelehnt; der Inhalt wird weder gespiegelt noch als Revision sichtbar.
    [Test]
    public async Task Draft_ShouldRejectPayloadAboveTheServerLimit()
    {
        using var context = await CreateContext();
        var task = await context.StartAsync("");
        using var client = context.CreateClient();
        var oversized = new string('x', 270_000);

        using var response = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 0,
            data = new { answer = oversized }
        });

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("task_draft.payload_too_large");
        problem.ToString().Should().NotContain(oversized);
        (await ReadDraft(client, task.Id)).GetProperty("revision").GetInt64().Should().Be(0);
    }

    // Testzweck: Der Aufgabenabschluss entfernt den privaten Entwurf zusammen mit der
    // Subscription; ein fehlgeschlagener Abschluss darf ihn dagegen nicht verlieren.
    [Test]
    public async Task Draft_ShouldBeRemovedOnlyAfterSuccessfulCompletion()
    {
        using var context = await CreateContext();
        var task = await context.StartAsync("");
        using var client = context.CreateClient();
        using var saved = await client.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 0,
            data = new { answer = "bleibt" }
        });
        saved.EnsureSuccessStatusCode();

        using var invalid = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            ProcessInstanceId = task.ProcessInstanceId,
            TokenId = task.Token.Id,
            FlowNodeId = "Review",
            Data = new ExpandoObject()
        });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadDraft(client, task.Id)).GetProperty("revision").GetInt64().Should().Be(1);

        var completedData = new ExpandoObject();
        ((IDictionary<string, object?>)completedData)["answer"] = "fertig";
        using var completed = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            ProcessInstanceId = task.ProcessInstanceId,
            TokenId = task.Token.Id,
            FlowNodeId = "Review",
            Data = completedData
        });
        using var gone = await client.GetAsync($"/usertask/{task.Id}/draft");

        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<AuthenticatedWorkflowTestContext> CreateContext()
    {
        var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", DraftForm);
        return context;
    }

    private static async Task<JsonElement> ReadDraft(HttpClient client, Guid taskId)
    {
        using var response = await client.GetAsync($"/usertask/{taskId}/draft");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Result(await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static JsonElement Result(JsonElement payload) => payload.GetProperty("result");
}
