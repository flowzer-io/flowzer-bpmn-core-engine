using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Flowzer.Shared;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

[NonParallelizable]
public sealed class WorkflowDraftIntegrationTest
{
    // Testzweck: Mehrere unvollständige Aufgaben erscheinen gemeinsam als anwählbare
    // Veröffentlichungsfehler, statt Nutzende nach jedem Fehler erneut prüfen zu lassen.
    [Test]
    public async Task PublishValidation_ShouldReturnIssuesForEachIncompleteTask()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isModeler: true);
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Draft">
              <process id="P" isExecutable="true"><userTask id="Human"/><serviceTask id="Worker"/></process>
            </definitions>
            """;
        using var response = await client.PostAsync("/definition/validate/deployment", new StringContent(xml, Encoding.UTF8, "application/xml"));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("issues").EnumerateArray().Select(issue => issue.GetProperty("elementId").GetString())
            .Should().BeEquivalentTo(["Human", "Worker"]);
    }

    // Testzweck: Fachlich unvollständige Entwürfe werden exakt gespeichert, ohne eine
    // aktive Version oder laufende Aufgaben zu verändern. Nur Publizieren bleibt gesperrt.
    [TestCase("<serviceTask id=\"Incomplete\" />")]
    [TestCase("<userTask id=\"Incomplete\" />")]
    [TestCase("<scriptTask id=\"Incomplete\" />")]
    [TestCase("<serviceTask id=\"Incomplete\"><extensionElements><f:aiTask /></extensionElements></serviceTask>")]
    [TestCase("<sequenceFlow id=\"Incomplete\" sourceRef=\"Missing\" targetRef=\"MissingToo\" />")]
    public async Task Save_ShouldAcceptIncompleteDraft_AndKeepDeployment(string content)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        var original = (await context.Storage.DefinitionStorage.GetAllDefinitions()).Single();
        var xml = $$"""
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:f="https://flowzer.io/schema/bpmn/1.0" id="{{original.DefinitionId}}">
              <process id="Draft" isExecutable="true">{{content}}</process>
            </definitions>
            """;
        using var client = context.CreateClient(isModeler: true);
        using var saved = await client.PostAsync("/definition", new StringContent(xml, Encoding.UTF8, "application/xml"));
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = (await saved.Content.ReadFromJsonAsync<ApiStatusResult<BpmnDefinitionDto>>())!.Result!;
        (await context.Storage.DefinitionStorage.GetBinary(draft.Id)).Should().Be(xml);
        (await context.Storage.DefinitionStorage.GetDefinitionById(draft.Id)).IsActive.Should().BeFalse();
        using var deploy = await client.PostAsync("/definition/deploy", new StringContent(xml, Encoding.UTF8, "application/xml"));
        deploy.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await context.Storage.DefinitionStorage.GetDeployedDefinition(original.DefinitionId))!.Id.Should().Be(original.Id);
        (await context.Storage.SubscriptionStorage.GetAllUserTasks(task.ProcessInstanceId!.Value)).Single().Id.Should().Be(task.Id);
    }
}
