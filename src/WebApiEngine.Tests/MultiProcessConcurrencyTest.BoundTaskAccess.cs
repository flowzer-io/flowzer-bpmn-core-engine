using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Model;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

public sealed partial class MultiProcessConcurrencyTest
{
    // Testzweck: Zwei reale PostgreSQL-/HTTP-Hosts rennen in 20 Runden auf Migration und
    // Claim/Release/Save. Ein Auftrag für V1 darf niemals Audit oder Entwurf unter V2 erzeugen.
    [TestCase("claim")][TestCase("release")][TestCase("save")]
    public async Task BoundMigrationAgainstTaskAction_ShouldNeverApplyTheOldBindingToTheTarget(string operation)
    {
        var source = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId, MultiProcessWorkflows.UserTask());
        var tasks = new List<UserTaskSubscription>();
        for (var round = 0; round < Rounds; round++)
        {
            var instance = await First.Engine.StartProcessInstance(source.DefinitionId);
            var task = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
            if (operation != "claim")
                (await PostAsync(_secondClient, $"/usertask/{task.Id}/claim", new { expectedRevision = 0 })).Should().Be(HttpStatusCode.OK);
            tasks.Add(task);
        }
        // Geänderter Form-Key wirft V1-Entwürfe bei Migration weg. Wäre ein V2-Entwurf
        // nach dem Rennen vorhanden, hätte der alte Save erst NACH Migration geschrieben.
        var target = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId,
            MultiProcessWorkflows.UserTask().Replace("formKey=\"Approval\"", "formKey=\"ApprovalV2\""), "ApprovalV2");
        foreach (var task in tasks)
        {
            var binding = BoundUserTaskAccessIntegrationTest.Binding(task, personal: operation != "claim");
            var responses = await RaceAsync(
                () => PostAsync(_firstClient, "/instance/migration", new InstanceMigrationRequestDto
                { InstanceIds = [task.ProcessInstanceId!.Value], TargetDefinitionId = target.Id }),
                () => operation switch
                {
                    "claim" => PostAsync(_secondClient, $"/usertask/{task.Id}/claim?" + binding, new { expectedRevision = 0 }),
                    "release" => PostAsync(_secondClient, $"/usertask/{task.Id}/release?" + binding, new { expectedRevision = 1, reason = "Freigeben" }),
                    _ => PutBoundDraftAsync(task, binding)
                });
            responses[0].Should().Be(HttpStatusCode.OK);
            responses[1].Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
            var instance = await First.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
            instance.DefinitionId.Should().Be(target.Id); instance.IsFinished.Should().BeFalse();
            var current = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().ContainSingle().Subject;
            current.Id.Should().Be(task.Id); current.DefinitionId.Should().Be(target.Id);
            var events = await First.Storage.UserTaskLifecycleStorage.GetEvents(task.Id);
            events.Should().OnlyContain(item => item.DefinitionId == source.Id, "kein alter Auftrag darf eine V2-Zuweisung verändern");
            (await First.Storage.UserTaskDraftStorage.CountForTask(task.Id)).Should().Be(0, "kein alter Save darf einen V2-Entwurf erzeugen");
        }
    }

    private async Task<HttpStatusCode> PutBoundDraftAsync(UserTaskSubscription task, string binding)
    {
        using var response = await _secondClient.PutAsJsonAsync($"/usertask/{task.Id}/draft?" + binding,
            new { expectedRevision = 0, expectedTaskRevision = 1, data = new { answer = "V1-Zwischenstand" } });
        return response.StatusCode;
    }
}
