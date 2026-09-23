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
              <process id="P" isExecutable="true"><scriptTask id="Script"/><serviceTask id="Worker"/></process>
            </definitions>
            """;
        using var response = await client.PostAsync("/definition/validate/deployment", new StringContent(xml, Encoding.UTF8, "application/xml"));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("issues").EnumerateArray().Select(issue => issue.GetProperty("elementId").GetString())
            .Should().BeEquivalentTo(["Script", "Worker"]);
    }

    // Testzweck: Fachlich unvollständige Entwürfe werden exakt gespeichert, ohne eine
    // aktive Version oder laufende Aufgaben zu verändern. Nur Publizieren bleibt gesperrt.
    [TestCase("<serviceTask id=\"Incomplete\" />")]
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

    // Testzweck: Ein User-Task ohne Formularbindung wird veröffentlicht und die Antwort nennt
    // den Verlust als Warnung — werkzeugneutrale Modelle sollen nicht mehr scheitern.
    [Test]
    public async Task Deploy_ShouldPublishUserTaskWithoutForm_AndReportItAsWarning()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var original = await context.DeployAsync("assignee=\"anna\"");
        var xml = $"""
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="{original.DefinitionId}">
              <process id="Draft" isExecutable="true">
                <startEvent id="Start" />
                <sequenceFlow id="ToHuman" sourceRef="Start" targetRef="Human" />
                <userTask id="Human" />
              </process>
            </definitions>
            """;
        using var client = context.CreateClient(isModeler: true);

        using var validation = await client.PostAsync("/definition/validate/deployment",
            new StringContent(xml, Encoding.UTF8, "application/xml"));
        validation.StatusCode.Should().Be(HttpStatusCode.OK);
        using var validationBody = System.Text.Json.JsonDocument.Parse(await validation.Content.ReadAsStringAsync());
        var warning = validationBody.RootElement.GetProperty("result").GetProperty("warnings")
            .EnumerateArray().Should().ContainSingle().Which;
        warning.GetProperty("code").GetString().Should().Be("bpmn.user_task.form_missing");
        warning.GetProperty("severity").GetString().Should().Be("warning");
        warning.GetProperty("elementId").GetString().Should().Be("Human");

        using var deploy = await client.PostAsync("/definition/deploy",
            new StringContent(xml, Encoding.UTF8, "application/xml"));
        deploy.StatusCode.Should().Be(HttpStatusCode.OK);
        var deployed = (await deploy.Content.ReadFromJsonAsync<ApiStatusResult<BpmnDefinitionDto>>())!.Result!;
        deployed.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be("bpmn.user_task.form_missing");
        (await context.Storage.DefinitionStorage.GetDeployedDefinition(original.DefinitionId))!.Id
            .Should().Be(deployed.Id);
    }
}
