using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using Flowzer.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Model;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>Explizite Scope-Ergebnisse müssen auch über persistierte Human-Task-Wartepunkte hinweg aktuell bleiben.</summary>
[NonParallelizable]
public sealed class SubProcessMappingPersistenceTest
{
    // Testzweck: JSON erhält keine Objekt-Aliase. Nach dem Start liest der HTTP-Abschluss
    // die Instanz neu und muss das frische lokale Ergebnis statt des Start-Snapshots exportieren.
    [Test]
    public async Task CompletedHumanTask_ShouldExportLatestMappedScopeAfterStorageReload()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "ScopedAnswer", """
            {"components":[{"type":"textfield","key":"answer","input":true,"validate":{"required":true}}]}
            """);
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(), DefinitionId = "scope-persistence", Hash = "synthetic-scope",
            SavedByUser = AuthenticatedWorkflowTestContext.UserId, SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0), IsActive = false
        };
        await context.Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            { DefinitionId = definition.DefinitionId, Name = "Synthetic scope persistence" });
        await context.Storage.DefinitionStorage.StoreDefinition(definition);
        await context.Storage.DefinitionStorage.StoreBinary(definition.Id, $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="scope-persistence" targetNamespace="test">
              <bpmn:process id="Process_Scope" isExecutable="true">
                <bpmn:startEvent id="Start" />
                <bpmn:sequenceFlow id="ToScope" sourceRef="Start" targetRef="Round" />
                <bpmn:subProcess id="Round">
                  <bpmn:extensionElements><zeebe:ioMapping>
                    <zeebe:input source="=marker" target="marker" />
                    <zeebe:output source="=vote" target="roundVote" />
                  </zeebe:ioMapping></bpmn:extensionElements>
                  <bpmn:startEvent id="RoundStart" />
                  <bpmn:sequenceFlow id="ToReview" sourceRef="RoundStart" targetRef="Review" />
                  <bpmn:userTask id="Review">
                    <bpmn:extensionElements>
                      <zeebe:formDefinition formKey="ScopedAnswer" />
                      <zeebe:assignmentDefinition assignee="{{AuthenticatedWorkflowTestContext.UserId}}" />
                      <zeebe:ioMapping><zeebe:output source="=answer" target="vote" /></zeebe:ioMapping>
                    </bpmn:extensionElements>
                  </bpmn:userTask>
                  <bpmn:sequenceFlow id="ReviewDone" sourceRef="Review" targetRef="RoundEnd" />
                  <bpmn:endEvent id="RoundEnd" />
                </bpmn:subProcess>
                <bpmn:sequenceFlow id="ToHold" sourceRef="Round" targetRef="Hold" />
                <bpmn:userTask id="Hold">
                  <bpmn:extensionElements>
                    <zeebe:formDefinition formKey="ScopedAnswer" />
                    <zeebe:assignmentDefinition assignee="{{AuthenticatedWorkflowTestContext.UserId}}" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:sequenceFlow id="ToEnd" sourceRef="Hold" targetRef="End" />
                <bpmn:endEvent id="End" />
              </bpmn:process>
            </bpmn:definitions>
            """);
        var business = context.Services.GetRequiredService<BpmnBusinessLogic>();
        await business.DeployDefinition(definition);
        var started = await business.StartProcessInstance(definition.DefinitionId,
            (ExpandoObject)ExpandoHelper.ToDynamic(new Dictionary<string, object?> { ["marker"] = "allowed", ["vote"] = "root-unchanged", ["private"] = "root-only" })!);
        var before = await context.Storage.InstanceStorage.GetProcessInstance(started.InstanceId);
        var scopeBefore = before.Tokens.Single(token => token.CurrentBaseElement.Id == "Round");
        ReferenceEquals(scopeBefore.Variables, scopeBefore.OutputData).Should().BeFalse("the test must cross real JSON persistence");
        var task = (await context.Storage.SubscriptionStorage.GetAllUserTasks(started.InstanceId)).Single();
        using var client = context.CreateClient();
        using var claim = await client.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 });
        claim.StatusCode.Should().Be(HttpStatusCode.OK);
        using var completion = await client.PostAsJsonAsync("/usertask", new
        {
            processInstanceId = started.InstanceId, tokenId = task.Token.Id, flowNodeId = "Review",
            expectedTaskRevision = 1, expectedUserTaskId = task.Id, expectedDefinitionId = task.DefinitionId,
            requireAssignedToCurrentUser = true, data = new { answer = "fresh" }
        });
        completion.StatusCode.Should().Be(HttpStatusCode.OK, await completion.Content.ReadAsStringAsync());
        var stored = await context.Storage.InstanceStorage.GetProcessInstance(started.InstanceId);
        var root = (IDictionary<string, object?>)stored.Tokens.Single(token => token.ParentTokenId is null).Variables!;
        root["roundVote"].Should().Be("fresh");
        root["vote"].Should().Be("root-unchanged");
        var scope = (IDictionary<string, object?>)stored.Tokens.Single(token => token.CurrentBaseElement.Id == "Round").Variables!;
        scope["vote"].Should().Be("fresh");
        scope.Should().NotContainKey("private");
    }
}
