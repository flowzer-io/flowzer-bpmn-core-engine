using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Model;
using WebApiEngine.Shared;

using CompletionContext = WebApiEngine.Tests.AuthenticatedWorkflowTestContext;

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
        // Vollständige Token-Diagnose ist jetzt dem Betrieb vorbehalten. Der Abschluss
        // selbst erfolgte weiterhin mit der normalen, nicht privilegierten Identität.
        using var diagnosticClient = context.CreateClient(isOperator: true);
        var payload = await diagnosticClient.GetFromJsonAsync<JsonElement>($"/instance/{instance.InstanceId}");
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

}
