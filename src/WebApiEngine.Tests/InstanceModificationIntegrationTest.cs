using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Der HTTP-Vertrag des Instanzeingriffs: Betriebsrecht, Statuscodes und die Wirkung, die ein
/// Betreiber danach in der Konsole sieht — neue Aufgabe, verschwundener Auftrag und ein
/// Laufzeitdiagramm, das den verlassenen Schritt als abgebrochen zeigt.
/// </summary>
[NonParallelizable]
public sealed class InstanceModificationIntegrationTest
{
    // Testzweck: Der vollstaendige Weg ueber HTTP. Der Trockenlauf nennt Schritte und Ziele, der
    // Eingriff verschiebt, die alte Aufgabe ist samt Kennung fort, und das Laufzeitdiagramm
    // zeigt den verlassenen Knoten als abgebrochen und das Ziel als aktiv.
    [Test]
    public async Task Modification_ShouldMoveAStepAndShowItInTheRuntimeDiagram()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceModificationScenarios.DeployAsync(provider, engine, InstanceModificationScenarios.Xml());
        var instance = await InstanceModificationScenarios.StartAsync(engine);
        var review = (await InstanceModificationScenarios.TasksAsync(provider, instance.InstanceId)).Single();

        using var client = context.CreateClient(isOperator: true);

        // Die leere Anfrage ist der Einstieg der Oberflaeche: Sie fragt, was ueberhaupt geht.
        var offer = await PreviewAsync(client, instance.InstanceId, new InstanceModificationRequestDto());
        using (new AssertionScope())
        {
            offer.Applicable.Should().BeTrue();
            offer.Steps.Should().ContainSingle().Which.Should().BeEquivalentTo(
                new { TokenId = review.Token.Id, FlowNodeId = "Review", Name = "Prüfung", Type = "UserTask" });
            offer.Targets.Select(target => target.Id).Should().NotContain("Start");
            offer.Targets.Should().Contain(target => target.Id == "Approve" && target.Name == "Freigabe");
        }

        var request = new InstanceModificationRequestDto
        {
            Moves = [new InstanceModificationMoveDto { TokenId = review.Token.Id, TargetFlowNodeId = "Approve" }]
        };

        var preview = await PreviewAsync(client, instance.InstanceId, request);
        using (new AssertionScope())
        {
            preview.Applicable.Should().BeTrue();
            preview.Problems.Should().BeEmpty();
            preview.Notices.Select(notice => notice.Code).Should().Contain("UserTaskCancelled");
        }

        // Der Trockenlauf hat nichts veraendert.
        (await InstanceModificationScenarios.TasksAsync(provider, instance.InstanceId))
            .Should().ContainSingle().Which.Id.Should().Be(review.Id);

        using var response = await client.PostAsJsonAsync($"/instance/{instance.InstanceId}/modification", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content
            .ReadFromJsonAsync<ApiStatusResult<InstanceModificationResultDto>>())!.Result!;

        using (new AssertionScope())
        {
            result.Modified.Should().BeTrue();
            result.Instance.InstanceId.Should().Be(instance.InstanceId);
            result.Instance.State.Should().Be(ProcessInstanceStateDto.Waiting);
        }

        var moved = (await InstanceModificationScenarios.TasksAsync(provider, instance.InstanceId))
            .Should().ContainSingle().Subject;
        moved.Id.Should().NotBe(review.Id);

        // Die alte Aufgabe gibt es wirklich nicht mehr — auch nicht ueber ihre eigene Route.
        using var goneTask = await client.GetAsync($"/usertask/{review.Id}");
        goneTask.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var diagram = (await client.GetFromJsonAsync<ApiStatusResult<RuntimeDiagramDto>>(
            $"/instance/{instance.InstanceId}/runtime-diagram"))!.Result!;
        using (new AssertionScope())
        {
            diagram.Nodes.Should().Contain(node =>
                node.FlowNodeId == "Review" && node.Status == RuntimeNodeStatusDto.Cancelled);
            diagram.Nodes.Should().Contain(node =>
                node.FlowNodeId == "Approve" && node.Status == RuntimeNodeStatusDto.Active);
        }

        // Die neue Aufgabe laesst sich abschliessen, und die Instanz laeuft am Ziel weiter.
        using var completed = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            ProcessInstanceId = instance.InstanceId,
            TokenId = moved.Token.Id,
            FlowNodeId = "Approve",
            Data = new ExpandoObject()
        });
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await InstanceModificationScenarios.JobsAsync(provider, instance.InstanceId))
            .Should().ContainSingle().Which.FlowNodeId.Should().Be("Notify");
    }

    // Testzweck: Ein Hindernis kommt als 422 mit denselben stabilen Codes wie im Trockenlauf
    // zurueck; ohne sie koennte die Oberflaeche den Fall nicht erklaeren.
    [Test]
    public async Task Modification_ShouldAnswerProblemsWithUnprocessableEntityAndStableCodes()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceModificationScenarios.DeployAsync(provider, engine, InstanceModificationScenarios.Xml());
        var instance = await InstanceModificationScenarios.StartAsync(engine);
        var review = (await InstanceModificationScenarios.TasksAsync(provider, instance.InstanceId)).Single();

        using var client = context.CreateClient(isOperator: true);
        using var response = await client.PostAsJsonAsync($"/instance/{instance.InstanceId}/modification",
            new InstanceModificationRequestDto
            {
                Moves = [new InstanceModificationMoveDto
                {
                    TokenId = review.Token.Id, TargetFlowNodeId = "Start"
                }]
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
        var problem = await response.Content
            .ReadFromJsonAsync<Middleware.InstanceModificationProblemDetails>();
        using (new AssertionScope())
        {
            problem.Should().NotBeNull();
            problem!.Problems.Should().ContainSingle().Which.Should()
                .Match<InstanceModificationFindingDto>(finding =>
                    finding.Code == "TargetNotAllowed" && finding.FlowNodeId == "Start");
            problem.Successful.Should().BeFalse();
        }

        (await InstanceModificationScenarios.TasksAsync(provider, instance.InstanceId))
            .Should().ContainSingle().Which.Id.Should().Be(review.Id);
    }

    // Testzweck: Eine leere Anfrage ist als Eingriff nichts; ohne diese Grenze schriebe ein
    // Klick ohne Auswahl eine Spur ohne Inhalt.
    [Test]
    public async Task Modification_ShouldRefuseAnEmptyChange()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceModificationScenarios.DeployAsync(provider, engine, InstanceModificationScenarios.Xml());
        var instance = await InstanceModificationScenarios.StartAsync(engine);

        using var client = context.CreateClient(isOperator: true);
        using var response = await client.PostAsJsonAsync(
            $"/instance/{instance.InstanceId}/modification", new InstanceModificationRequestDto());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
    }

    // Testzweck: An einer beendeten Instanz gibt es nichts zu ruecken. Das ist ein
    // Zustandskonflikt (409) und keine unbrauchbare Angabe (422) — die Oberflaeche erklaert
    // beides verschieden.
    [Test]
    public async Task Modification_ShouldAnswerConflictForAFinishedInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceModificationScenarios.DeployAsync(provider, engine, InstanceModificationScenarios.Xml());
        var instance = await InstanceModificationScenarios.StartAsync(engine);
        var review = (await InstanceModificationScenarios.TasksAsync(provider, instance.InstanceId)).Single();

        using var client = context.CreateClient(isOperator: true);
        using var cancelled = await client.PostAsync($"/instance/{instance.InstanceId}/cancel", null);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);

        var request = new InstanceModificationRequestDto
        {
            Moves = [new InstanceModificationMoveDto
            {
                TokenId = review.Token.Id, TargetFlowNodeId = "Approve"
            }]
        };

        using (new AssertionScope())
        {
            using var preview = await client.PostAsJsonAsync(
                $"/instance/{instance.InstanceId}/modification/preview", request);
            preview.StatusCode.Should().Be(HttpStatusCode.Conflict);

            using var modify = await client.PostAsJsonAsync(
                $"/instance/{instance.InstanceId}/modification", request);
            modify.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    // Testzweck: Eine Instanz, die es nicht gibt, ist 404 — nicht 422 und nicht 500.
    [Test]
    public async Task Modification_ShouldAnswerNotFoundForAnUnknownInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isOperator: true);

        using var response = await client.PostAsJsonAsync($"/instance/{Guid.NewGuid()}/modification/preview",
            new InstanceModificationRequestDto());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Der Eingriff greift in fremde Vorgaenge ein und bleibt deshalb dem Betrieb
    // vorbehalten — auch der Trockenlauf, denn er legt den Tokenstand offen.
    [Test]
    public async Task Modification_ShouldRequireTheOperatorRole()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var provider = new FileSystemTransactionalStorageProvider();
        var engine = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await InstanceModificationScenarios.DeployAsync(provider, engine, InstanceModificationScenarios.Xml());
        var instance = await InstanceModificationScenarios.StartAsync(engine);
        var review = (await InstanceModificationScenarios.TasksAsync(provider, instance.InstanceId)).Single();

        using var withoutRole = context.CreateClient(isOperator: false);
        var request = new InstanceModificationRequestDto
        {
            Moves = [new InstanceModificationMoveDto
            {
                TokenId = review.Token.Id, TargetFlowNodeId = "Approve"
            }]
        };

        using (new AssertionScope())
        {
            using var preview = await withoutRole.PostAsJsonAsync(
                $"/instance/{instance.InstanceId}/modification/preview", request);
            preview.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var modify = await withoutRole.PostAsJsonAsync(
                $"/instance/{instance.InstanceId}/modification", request);
            modify.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // Und die Instanz steht unveraendert da.
        (await InstanceModificationScenarios.TasksAsync(provider, instance.InstanceId))
            .Should().ContainSingle().Which.Id.Should().Be(review.Id);
    }

    private static async Task<InstanceModificationPreviewDto> PreviewAsync(
        HttpClient client, Guid instanceId, InstanceModificationRequestDto request)
    {
        using var response = await client.PostAsJsonAsync(
            $"/instance/{instanceId}/modification/preview", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content
            .ReadFromJsonAsync<ApiStatusResult<InstanceModificationPreviewDto>>())!.Result!;
    }
}
