using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Eine menschliche Aufgabe ohne Formularbindung. Sie ist eine gewöhnliche Human Task —
/// nur gibt es nichts auszufüllen: Das Formular fehlt, der Abschluss bleibt.
/// </summary>
[NonParallelizable]
public sealed class UserTaskWithoutFormIntegrationTest
{
    // Testzweck: Eine Aufgabe ohne Formularbindung wird übernommen, ohne Formulardaten
    // abgeschlossen, und der Prozess läuft danach bis zum Ende weiter.
    [Test]
    public async Task TaskWithoutForm_ShouldBeClaimedAndCompleted_AndLetTheProcessContinue()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("candidateGroups=\"review\"", taskFormKey: null);
        using var client = context.CreateClient(username: "bert");

        using var claimed = await client.PostAsJsonAsync($"/usertask/{task.Id}/claim",
            new { expectedRevision = 0 });
        claimed.StatusCode.Should().Be(HttpStatusCode.OK);

        using var completed = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            ProcessInstanceId = task.ProcessInstanceId,
            TokenId = task.Token.Id,
            FlowNodeId = task.Token.CurrentFlowNode!.Id
        });
        completed.StatusCode.Should().Be(HttpStatusCode.OK);

        var instance = await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
        instance.IsFinished.Should().BeTrue();
        (await context.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().BeEmpty();
    }

    // Testzweck: Kein Formular ist eine Auskunft, kein Fehler. Die Oberfläche soll an 204
    // erkennen, dass sie sofort abschließen lässt, statt einen leeren Dialog zu zeigen.
    [Test]
    public async Task Form_ShouldAnswerWithNoContent_WhenTheTaskBindsNoForm()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"", taskFormKey: null);
        using var client = context.CreateClient(username: "bert");

        using var response = await client.GetAsync($"/usertask/{task.Id}/form");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // Testzweck: Ohne Formular ist die Dokumentation des Knotens die einzige Erklärung der
    // Aufgabe. Sie gehört deshalb flach ins Aufgaben-DTO und nicht ins Flow-Element.
    [Test]
    public async Task Task_ShouldExposeTheModelDocumentation()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var definition = await context.DeployAsync("assignee=\"bert\"", taskFormKey: null,
            taskDocumentation: "Bitte die Rechnung sachlich prüfen.");
        var engine = context.Services
            .GetRequiredService<WebApiEngine.BusinessLogic.BpmnBusinessLogic>();
        var instance = await engine.StartProcessInstance(definition.DefinitionId);
        var task = (await context.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
        using var client = context.CreateClient(username: "bert");

        using var response = await client.GetAsync($"/usertask/{task.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = payload.GetProperty("result");
        result.GetProperty("documentation").GetString().Should().Be("Bitte die Rechnung sachlich prüfen.");
        result.GetProperty("formKey").GetString().Should().BeEmpty();
    }

    // Testzweck: Eine Aufgabe mit Formular behält ihr Formular — die Lockerung darf den
    // bestehenden Weg nicht stillschweigend mit abschalten.
    [Test]
    public async Task Form_ShouldStillBeDeliveredForATaskWithABinding()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        using var client = context.CreateClient(username: "bert");

        using var response = await client.GetAsync($"/usertask/{task.Id}/form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
