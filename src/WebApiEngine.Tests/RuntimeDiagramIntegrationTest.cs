using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Flowzer.Shared;
using Microsoft.Extensions.DependencyInjection;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Der technische Laufzeitblick bleibt objektberechtigt, versionstreu und datensparsam.</summary>
[NonParallelizable]
public sealed class RuntimeDiagramIntegrationTest
{
    // Testzweck: Nur der Betrieb darf das Laufzeitdiagramm lesen; fremde und fehlende
    // Instanzen bleiben über denselben neutralen Problem-Details-Vertrag ununterscheidbar.
    [Test]
    public async Task RuntimeDiagram_ShouldRequireObjectScopedOperatorAccess()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var ordinaryUser = context.CreateClient();
        using var operatorClient = context.CreateClient(isOperator: true);

        using var foreign = await ordinaryUser.GetAsync($"/instance/{task.ProcessInstanceId}/runtime-diagram");
        using var missing = await operatorClient.GetAsync($"/instance/{Guid.NewGuid()}/runtime-diagram");

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreign.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var foreignProblem = await foreign.Content.ReadFromJsonAsync<JsonElement>();
        var missingProblem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        foreignProblem.GetProperty("title").GetString().Should().Be(missingProblem.GetProperty("title").GetString());
        foreignProblem.GetProperty("detail").GetString().Should().Be(missingProblem.GetProperty("detail").GetString());
    }

    // Testzweck: Die Projektion verwendet die beim Start gebundene Definition und liefert
    // echte persistierte Engine-Ereignisse, ohne Tokens, Variablen, Personen oder Erweiterungen.
    [Test]
    public async Task RuntimeDiagram_ShouldReturnBoundSanitizedDiagramAndPersistedEvents()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"TOP-SECRET-ASSIGNMENT\"");
        using var client = context.CreateClient(isOperator: true);

        using var response = await client.GetAsync($"/instance/{task.ProcessInstanceId}/runtime-diagram");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<RuntimeDiagramDto>>();
        payload!.Result.Should().NotBeNull();
        payload.Result!.InstanceId.Should().Be(task.ProcessInstanceId!.Value);
        payload.Result.DefinitionId.Should().Be(task.DefinitionId);
        payload.Result.ProcessId.Should().Be("Process_Completion");
        payload.Result.DiagramXml.Should().Contain("Process_Completion").And.Contain("Review");
        payload.Result.DiagramXml.Should().NotContain("TOP-SECRET-ASSIGNMENT")
            .And.NotContain("extensionElements")
            .And.NotContain("assignmentDefinition");
        payload.Result.Nodes.Should().Contain(node =>
            node.FlowNodeId == "Review" && node.Status == RuntimeNodeStatusDto.Active);
        payload.Result.Events.Should().Contain(item =>
            item.FlowNodeId == "Review" && item.State == FlowNodeStateDto.Active);

        var json = await response.Content.ReadAsStringAsync();
        json.ToLowerInvariant().Should().NotContain("variables")
            .And.NotContain("outputdata")
            .And.NotContain("completedbyuserid")
            .And.NotContain("tokenid")
            .And.NotContain("correlationid")
            .And.NotContain("actor");
    }

    // Testzweck: Mehrere Token desselben Knotens werden ohne erfundene Schrittzahl zu
    // einem Status verdichtet; Fehler haben Vorrang vor aktiv und beendet.
    [Test]
    public async Task RuntimeNodeProjection_ShouldAggregateParallelStatesDeterministically()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        var now = DateTimeOffset.Parse("2026-09-09T12:00:00Z");
        foreach (var (state, occurredAtUtc) in new[]
        {
            (Model.FlowNodeState.Completed, now.AddSeconds(-2)),
            (Model.FlowNodeState.Active, now.AddSeconds(-1)),
            (Model.FlowNodeState.Failed, now)
        })
        {
            await context.Storage.RuntimeNodeEventStorage.AppendIfAbsent(new Model.RuntimeNodeEvent
            {
                Id = Guid.NewGuid(),
                ProcessInstanceId = task.ProcessInstanceId!.Value,
                DefinitionId = task.DefinitionId,
                TokenId = Guid.NewGuid(),
                FlowNodeId = "Parallel",
                State = state,
                CorrelationId = Guid.NewGuid(),
                OccurredAtUtc = occurredAtUtc
            });
        }
        using var client = context.CreateClient(isOperator: true);

        var payload = await client.GetFromJsonAsync<ApiStatusResult<RuntimeDiagramDto>>(
            $"/instance/{task.ProcessInstanceId}/runtime-diagram");

        var parallel = payload!.Result!.Nodes.Single(node => node.FlowNodeId == "Parallel");
        parallel.Status.Should().Be(RuntimeNodeStatusDto.Failed);
        parallel.TokenCount.Should().Be(3);
        parallel.LastChangedAtUtc.Should().Be(now);
    }

    // Testzweck: Ein späteres Deployment darf das Diagramm einer laufenden Instanz
    // nicht auf „latest“ umbiegen; DefinitionId und ProcessId bleiben exakt gebunden.
    [Test]
    public async Task RuntimeDiagram_ShouldKeepTheDefinitionBoundAtStart()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        var newer = new BpmnDefinition
        {
            Id = Guid.NewGuid(),
            DefinitionId = "Definitions_Completion",
            Hash = "newer",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(2, 0),
            IsActive = false
        };
        await context.Storage.DefinitionStorage.StoreDefinition(newer);
        await context.Storage.DefinitionStorage.StoreBinary(newer.Id, """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_Newer" targetNamespace="test">
              <bpmn:process id="Process_Newer" isExecutable="true">
                <bpmn:startEvent id="NewStart"><bpmn:outgoing>NewFlow</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="NewFlow" sourceRef="NewStart" targetRef="NewEnd" />
                <bpmn:endEvent id="NewEnd"><bpmn:incoming>NewFlow</bpmn:incoming></bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """);
        await context.Services.GetRequiredService<BpmnBusinessLogic>().DeployDefinition(newer);
        using var client = context.CreateClient(isOperator: true);

        var payload = await client.GetFromJsonAsync<ApiStatusResult<RuntimeDiagramDto>>(
            $"/instance/{task.ProcessInstanceId}/runtime-diagram");

        payload!.Result!.DefinitionId.Should().Be(task.DefinitionId);
        payload.Result.ProcessId.Should().Be("Process_Completion");
        payload.Result.DiagramXml.Should().Contain("Process_Completion").And.NotContain("Process_Newer");
    }

    // Testzweck: Jeder mutierende Persistenzweg schreibt neue Engine-Zustände in die
    // append-only Spur; ein Taskabschluss erscheint als echter Zustand statt Momentaufnahme.
    [Test]
    public async Task RuntimeDiagram_ShouldAppendEventsAfterTaskCompletion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        using var worker = context.CreateClient();
        using var completion = await worker.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            ProcessInstanceId = task.ProcessInstanceId,
            TokenId = task.Token.Id,
            FlowNodeId = "Review",
            ExpectedTaskRevision = 0
        });
        completion.EnsureSuccessStatusCode();
        using var client = context.CreateClient(isOperator: true);

        var payload = await client.GetFromJsonAsync<ApiStatusResult<RuntimeDiagramDto>>(
            $"/instance/{task.ProcessInstanceId}/runtime-diagram");

        payload!.Result!.Events.Should().Contain(item =>
            item.FlowNodeId == "Review" && item.State == FlowNodeStateDto.Completed);
        payload.Result.Events.Should().Contain(item =>
            item.FlowNodeId == "End" && item.State == FlowNodeStateDto.Completed);
        payload.Result.State.Should().Be(ProcessInstanceStateDto.Completed);
    }

    // Testzweck: Eine beschädigte historische ProcessId-Bindung fällt neutral auf 404
    // zurück und verwendet weder irgendeinen anderen Prozess noch die neueste Version.
    [Test]
    public async Task RuntimeDiagram_ShouldNotFallBackWhenBoundProcessIsMissing()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        var stored = await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
        await context.Storage.InstanceStorage.AddOrUpdateInstance(new ProcessInstanceInfo
        {
            InstanceId = Guid.NewGuid(),
            metaDefinitionId = stored.metaDefinitionId,
            DefinitionId = stored.DefinitionId,
            ProcessId = "Process_Missing",
            Tokens = stored.Tokens,
            IsFinished = stored.IsFinished,
            State = stored.State,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 0,
            ServiceSubscriptionCount = 0
        });
        var brokenId = (await context.Storage.InstanceStorage.GetAllInstances())
            .Single(instance => instance.ProcessId == "Process_Missing").InstanceId;
        using var client = context.CreateClient(isOperator: true);

        using var response = await client.GetAsync($"/instance/{brokenId}/runtime-diagram");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}
