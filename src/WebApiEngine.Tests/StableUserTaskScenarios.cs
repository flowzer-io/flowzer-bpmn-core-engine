using System.Dynamic;
using BPMN.HumanInteraction;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>Identische Runtime-Vertragsfälle für isolierte Datei- und PostgreSQL-Ablage.</summary>
internal static class StableUserTaskScenarios
{
    internal static async Task ProgressAsync(ITransactionalStorageProvider provider)
    {
        var (engine, instance) = await StartAsync(provider);
        var initial = await TasksAsync(provider, instance.InstanceId);
        var right = initial.Single(task => task.Token.CurrentFlowNode!.Id == "Right");
        var first = initial.Single(task => task.Token.CurrentFlowNode!.Id == "First");
        var assigned = Guid.NewGuid();
        using (var storage = provider.GetTransactionalStorage())
        {
            // Zukunftsfähige Erhaltung bereits persistierter Metadaten, keine neue Claim-API.
            right.Assignee = "explicit-text";
            right.CandidateUsers = ["text-user"];
            right.CandidateGroups = ["/team/same-name"];
            right.CurrenAssignedUser = assigned;
            right.UserCandidates = [assigned];
            right.UserGroups = [assigned];
            await storage.SubscriptionStorage.AddUserTaskSubscription(right);
            var persisted = await storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
            var token = persisted.Tokens.Single(candidate => candidate.Id == right.Token.Id);
            dynamic data = new ExpandoObject();
            data.current = "new context";
            token.Variables = data;
            await storage.InstanceStorage.AddOrUpdateInstance(persisted);
            storage.CommitChanges();
        }

        // Neue Engine ohne In-Memory-Zustand: Identität kommt ausschließlich aus der Ablage.
        engine = new BpmnBusinessLogic(provider);
        await CompleteAsync(engine, first);
        var after = await TasksAsync(provider, instance.InstanceId);
        after.Should().HaveCount(2);
        var kept = after.Single(task => task.Token.Id == right.Token.Id);
        kept.Id.Should().Be(right.Id);
        kept.Assignee.Should().Be("explicit-text");
        kept.CandidateUsers.Should().Equal("text-user");
        kept.CandidateGroups.Should().Equal("/team/same-name");
        kept.CurrenAssignedUser.Should().Be(assigned);
        kept.UserCandidates.Should().Equal(assigned);
        kept.UserGroups.Should().Equal(assigned);
        ((IDictionary<string, object?>)kept.Token.Variables!)["current"].Should().Be("new context");
        after.Should().NotContain(task => task.Id == first.Id);
        var second = after.Single(task => task.Token.CurrentFlowNode!.Id == "Second");
        second.Id.Should().NotBe(first.Id).And.NotBe(right.Id);
        using (var storage = provider.GetTransactionalStorage())
            (await storage.SubscriptionStorage.GetUserTaskExtended(right.Id)).Should().NotBeNull();
        await CompleteAsync(engine, kept);
        (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Which.Id.Should().Be(second.Id);
        await CompleteAsync(engine, second);
        (await TasksAsync(provider, instance.InstanceId)).Should().BeEmpty();
    }

    internal static async Task TimerAsync(ITransactionalStorageProvider provider)
    {
        var (engine, instance) = await StartAsync(provider, timer: true);
        var right = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        engine = new BpmnBusinessLogic(provider);
        (await engine.HandleTime(DateTime.UtcNow.AddMinutes(2))).Should().Be(1);
        var after = await TasksAsync(provider, instance.InstanceId);
        after.Should().HaveCount(2);
        after.Single(task => task.Token.Id == right.Token.Id).Id.Should().Be(right.Id);
        after.Single(task => task.Token.CurrentFlowNode!.Id == "Second").Id.Should().NotBe(right.Id);
    }

    internal static async Task CancelAsync(ITransactionalStorageProvider provider)
    {
        var (engine, first) = await StartAsync(provider);
        var second = await engine.StartProcessInstance(first.metaDefinitionId);
        var otherIds = (await TasksAsync(provider, second.InstanceId)).Select(task => task.Id).ToArray();
        await engine.CancelInstance(first.InstanceId);
        (await TasksAsync(provider, first.InstanceId)).Should().BeEmpty();
        (await TasksAsync(provider, second.InstanceId)).Select(task => task.Id).Should().BeEquivalentTo(otherIds);
    }

    internal static async Task CorruptAsync(ITransactionalStorageProvider provider, bool duplicate)
    {
        var (engine, instance) = await StartAsync(provider);
        var initial = await TasksAsync(provider, instance.InstanceId);
        var right = initial.Single(task => task.Token.CurrentFlowNode!.Id == "Right");
        using (var storage = provider.GetTransactionalStorage())
        {
            if (duplicate) right.Id = Guid.NewGuid();
            else right.DefinitionId = Guid.NewGuid();
            await storage.SubscriptionStorage.AddUserTaskSubscription(right);
            storage.CommitChanges();
        }
        var before = (await TasksAsync(provider, instance.InstanceId)).Select(task => task.Id).ToArray();
        Func<Task> finish = () => CompleteAsync(engine, initial.Single(task => task.Token.CurrentFlowNode!.Id == "First"));
        await finish.Should().ThrowAsync<InvalidOperationException>().WithMessage("*task*identity*");
        (await TasksAsync(provider, instance.InstanceId)).Select(task => task.Id).Should().BeEquivalentTo(before);
        using (var storage = provider.GetTransactionalStorage())
            (await storage.InstanceStorage.GetProcessInstance(instance.InstanceId)).Tokens
                .Single(token => token.CurrentFlowNode?.Id == "First").State.Should().Be(FlowNodeState.Active);
    }

    internal static async Task DirectoryProgressAsync(ITransactionalStorageProvider provider)
    {
        var (userId, groupId) = await PublishDirectoryAsync(provider);
        var (engine, instance) = await StartAsync(provider, directoryAssignment: (userId, groupId));
        var initial = await TasksAsync(provider, instance.InstanceId);
        var right = initial.Single(task => task.Token.CurrentFlowNode!.Id == "Right");
        var first = initial.Single(task => task.Token.CurrentFlowNode!.Id == "First");

        // Neue Engine und neue Storage-Session erzwingen den dauerhaften Roundtrip.
        engine = new BpmnBusinessLogic(provider);
        await CompleteAsync(engine, first);
        var kept = (await TasksAsync(provider, instance.InstanceId))
            .Single(task => task.Token.Id == right.Token.Id);

        kept.Id.Should().Be(right.Id);
        kept.AssignmentMode.Should().Be(UserTaskAssignmentMode.Directory);
        kept.DirectoryAssigneeUserId.Should().Be(userId);
        kept.DirectoryCandidateGroupIds.Should().Equal(groupId);
        kept.Assignee.Should().BeNull();
        kept.CandidateUsers.Should().BeEmpty();
        kept.CandidateGroups.Should().BeEmpty();
    }

    private static async Task CompleteAsync(BpmnBusinessLogic engine, UserTaskSubscription task)
    {
        var result = await engine.CompleteUserTaskAsync(new UserTaskResult
        {
            ProcessInstanceId = task.ProcessInstanceId, TokenId = task.Token.Id,
            FlowNodeId = task.Token.CurrentFlowNode!.Id, Data = new ExpandoObject()
        }, new CurrentUserContext(Guid.NewGuid(), "isolated-test", false), canOperateAllTasks: true);
        result.Should().Be(UserTaskCompletionOutcome.Completed);
    }

    private static async Task<UserTaskSubscription[]> TasksAsync(ITransactionalStorageProvider provider, Guid id)
    {
        using var storage = provider.GetTransactionalStorage();
        return (await storage.SubscriptionStorage.GetAllUserTasks(id)).ToArray();
    }

    private static async Task<(BpmnBusinessLogic Engine, ProcessInstanceInfo Instance)> StartAsync(
        ITransactionalStorageProvider provider,
        bool timer = false,
        (Guid UserId, Guid GroupId)? directoryAssignment = null)
    {
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(), DefinitionId = "Definitions_Stable", Version = new Model.Version(1, 0),
            Hash = "test", SavedByUser = Guid.NewGuid(), SavedOn = DateTime.UtcNow, IsActive = false
        };
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = definition.DefinitionId, Name = "Stable tasks" });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, Xml(timer, directoryAssignment));
            await FormTestSeed.StoreAsync(storage, "Approval");
            storage.CommitChanges();
        }
        var engine = new BpmnBusinessLogic(provider);
        await engine.DeployDefinition(definition);
        return (engine, await engine.StartProcessInstance(definition.DefinitionId));
    }

    private static string Xml(bool timer, (Guid UserId, Guid GroupId)? directoryAssignment = null)
    {
        var first = timer ? """
            <bpmn:intermediateCatchEvent id="First"><bpmn:timerEventDefinition><bpmn:timeDuration>PT1S</bpmn:timeDuration></bpmn:timerEventDefinition></bpmn:intermediateCatchEvent>
            """ : TaskXml("First", directoryAssignment);
        return $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
              xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
              id="Definitions_Stable" targetNamespace="test">
              <bpmn:process id="Process_Stable" isExecutable="true">
                <bpmn:startEvent id="Start" />
                <bpmn:parallelGateway id="Fork" />
                {{first}}
                {{TaskXml("Second", directoryAssignment)}}
                {{TaskXml("Right", directoryAssignment)}}
                <bpmn:parallelGateway id="Join" />
                <bpmn:endEvent id="End" />
                <bpmn:sequenceFlow id="F0" sourceRef="Start" targetRef="Fork" />
                <bpmn:sequenceFlow id="F1" sourceRef="Fork" targetRef="First" />
                <bpmn:sequenceFlow id="F2" sourceRef="Fork" targetRef="Right" />
                <bpmn:sequenceFlow id="F3" sourceRef="First" targetRef="Second" />
                <bpmn:sequenceFlow id="F4" sourceRef="Second" targetRef="Join" />
                <bpmn:sequenceFlow id="F5" sourceRef="Right" targetRef="Join" />
                <bpmn:sequenceFlow id="F6" sourceRef="Join" targetRef="End" />
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    private static string TaskXml(string id, (Guid UserId, Guid GroupId)? directoryAssignment = null) => $$"""
        <bpmn:userTask id="{{id}}" name="{{id}}"><bpmn:extensionElements>
          <zeebe:formDefinition formKey="Approval" />
          {{(directoryAssignment is { } directory
              ? $"<flowzer:taskAssignment mode=\"directory\" assigneeId=\"{directory.UserId}\" candidateGroupIds=\"{directory.GroupId}\" />"
              : "<zeebe:assignmentDefinition assignee=\"model-text\" candidateUsers=\"text-user\" candidateGroups=\"/team/original\" />")}}
        </bpmn:extensionElements></bpmn:userTask>
        """;

    private static async Task<(Guid UserId, Guid GroupId)> PublishDirectoryAsync(
        ITransactionalStorageProvider provider)
    {
        const string issuer = "https://issuer.test/realms/stable";
        var importedUserId = Guid.NewGuid();
        var importedGroupId = Guid.NewGuid();
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = issuer, CompletedAtUtc = DateTime.UtcNow,
            Users =
            [
                new DirectoryUser
                {
                    Id = importedUserId, SourceKind = DirectorySourceKind.Keycloak, Issuer = issuer,
                    Subject = "stable-user", DisplayName = "Stable User", IsActive = true
                }
            ],
            Groups =
            [
                new DirectoryGroup
                {
                    Id = importedGroupId, SourceKind = DirectorySourceKind.Keycloak, Issuer = issuer,
                    ExternalId = "stable-group", Name = "Stable Group", Path = "/stable", IsActive = true
                }
            ],
            Memberships = [new DirectoryMembership { UserId = importedUserId, GroupId = importedGroupId }]
        };
        using (var storage = provider.GetTransactionalStorage())
        {
            var startedAt = snapshot.CompletedAtUtc.AddSeconds(-1);
            (await storage.IdentityDirectoryStorage.TryStartSync(
                issuer, snapshot.GenerationId, startedAt, startedAt.AddMinutes(5))).Should().BeTrue();
            await storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
            storage.CommitChanges();
        }

        using (var storage = provider.GetTransactionalStorage())
        {
            var published = (await storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
            return (published.Users.Single(user => user.Subject == "stable-user").Id,
                published.Groups.Single(group => group.ExternalId == "stable-group").Id);
        }
    }
}
