using System.Net;
using FluentAssertions;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Gebundener Hostabschluss gegen eine gleichzeitig migrierende echte PostgreSQL-Instanz.</summary>
public sealed partial class MultiProcessConcurrencyTest
{
    // Testzweck: Zwei getrennte HTTP-Hosts rennen auf derselben PostgreSQL-Instanz.
    // Entweder schließt das V1-Formular wirklich V1 ab, oder Migration gewinnt und V1
    // bleibt ohne Entscheidung zurückgewiesen; niemals darf es V2 abschließen.
    [Test]
    public async Task BoundMigrationAgainstCompletion_ShouldNeverCompleteTheTargetWithTheSourceForm()
    {
        var source = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId,
            MultiProcessWorkflows.UserTask());
        var instances = new List<ProcessInstanceInfo>();
        for (var round = 0; round < Rounds; round++)
        {
            var instance = await First.Engine.StartProcessInstance(source.DefinitionId);
            var task = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
            (await PostAsync(_secondClient, $"/usertask/{task.Id}/claim", new { expectedRevision = 0 }))
                .Should().Be(HttpStatusCode.OK);
            instances.Add(instance);
        }
        var target = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId,
            MultiProcessWorkflows.UserTask());
        foreach (var instance in instances)
        {
            var task = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
            var result = new UserTaskResultDto
            {
                FlowNodeId = "Review", TokenId = task.Token.Id, ProcessInstanceId = instance.InstanceId,
                ExpectedUserTaskId = task.Id, ExpectedDefinitionId = source.Id,
                ExpectedTaskRevision = 1, RequireAssignedToCurrentUser = true
            };
            var responses = await RaceAsync(
                () => PostAsync(_firstClient, "/instance/migration", new InstanceMigrationRequestDto
                {
                    InstanceIds = [instance.InstanceId], TargetDefinitionId = target.Id
                }),
                () => PostAsync(_secondClient, "/usertask", result));
            var stored = await First.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
            var waiting = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).ToArray();
            var events = await First.Storage.UserTaskLifecycleStorage.GetEvents(task.Id);
            if (responses[1] == HttpStatusCode.OK)
            {
                stored.DefinitionId.Should().Be(source.Id, "ein V1-Abschluss darf niemals V2 entscheiden");
                stored.IsFinished.Should().BeTrue();
                waiting.Should().BeEmpty();
                events.Select(item => item.Action).Should().Equal("claim", "complete");
            }
            else
            {
                responses[1].Should().Be(HttpStatusCode.Conflict);
                stored.DefinitionId.Should().Be(target.Id);
                stored.IsFinished.Should().BeFalse();
                waiting.Should().ContainSingle().Which.Id.Should().Be(task.Id);
                events.Select(item => item.Action).Should().Equal("claim");
            }
        }
    }
}
