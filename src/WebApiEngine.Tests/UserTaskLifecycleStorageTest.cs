using BPMN.HumanInteraction;
using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>Belegt CAS, Aufräumen und dauerhafte Auditspur der Entwicklungsablage.</summary>
[NonParallelizable]
public sealed class UserTaskLifecycleStorageTest
{
    // Testzweck: Tatsächlich fehlende neue Actorfelder im Alt-JSON bleiben lesbar;
    // weder Vermittler noch verifizierte Identität werden aus historischen GUIDs erfunden.
    [Test]
    public async Task LegacyJsonWithoutActorMetadata_ShouldRemainReadable()
    {
        using var context = new Context();
        var task = await context.AddTask();
        var item = Create(task, "claim", revision: 1).Event;
        var oldJson = Newtonsoft.Json.Linq.JObject.FromObject(item);
        oldJson.Remove(nameof(UserTaskAssignmentEvent.AuthenticatedActor)).Should().BeTrue();
        var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<UserTaskAssignmentEvent>(oldJson.ToString());
        loaded.Should().NotBeNull();
        loaded!.AuthenticatedActor.Should().BeNull();
        loaded.ActorUserId.Should().Be(item.ActorUserId);
        // Auch ein tatsächlicher polymorpher Task-Token im bisherigen Speicherformat
        // muss ohne neue Felder laden, nicht nur ein künstliches leeres JSON-Objekt.
        var tokenJson = Newtonsoft.Json.Linq.JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(
            task.Token, context.Storage.NewtonSoftDefaultSettings));
        tokenJson.Remove(nameof(Token.CompletedByActor)).Should().BeTrue();
        tokenJson.Remove(nameof(Token.Withdrawal)).Should().BeTrue();
        var token = tokenJson.ToObject<Token>(Newtonsoft.Json.JsonSerializer.Create(context.Storage.NewtonSoftDefaultSettings));
        token.Should().NotBeNull();
        token!.Id.Should().Be(task.Token.Id);
        token.CurrentBaseElement.Should().BeOfType<UserTask>();
        token.CompletedByActor.Should().BeNull();
        token.Withdrawal.Should().BeNull();
    }

    // Testzweck: Zwei gleichzeitige Zustandswechsel derselben Revision haben genau einen
    // Gewinner und erzeugen deshalb auch nur ein Auditereignis.
    [Test]
    public async Task ConcurrentClaims_ShouldWriteExactlyOneStateAndEvent()
    {
        using var context = new Context();
        var task = await context.AddTask();
        var first = Create(task, "claim", revision: 1);
        var second = Create(task, "claim", revision: 1);

        var results = await Task.WhenAll(
            context.Storage.UserTaskLifecycleStorage.TryWrite(first.State, 0, first.Event),
            context.Storage.UserTaskLifecycleStorage.TryWrite(second.State, 0, second.Event));

        results.Should().ContainSingle(result => result.Status == UserTaskLifecycleWriteStatus.Written);
        results.Should().ContainSingle(result => result.Status == UserTaskLifecycleWriteStatus.RevisionConflict);
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().ContainSingle();
    }

    // Testzweck: Task-Löschung entfernt den aktuellen Zustand, bewahrt aber die
    // revisionsgebundene Auditspur für die spätere Vorgangshistorie.
    [Test]
    public async Task RemovingTask_ShouldDeleteStateButRetainAudit()
    {
        using var context = new Context();
        var task = await context.AddTask();
        var item = Create(task, "claim", revision: 1);
        await context.Storage.UserTaskLifecycleStorage.TryWrite(item.State, 0, item.Event);

        await context.Storage.SubscriptionStorage.RemoveUserTaskSubscription(task.Id);

        (await context.Storage.UserTaskLifecycleStorage.Get(task.Id)).Should().BeNull();
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().ContainSingle()
            .Which.Action.Should().Be("claim");
    }

    // Testzweck: Die Vorgangssicht liefert nur die Auditspur der angefragten Instanz und
    // bewahrt diese auch dann, wenn die zugehörige offene Task-Subscription bereits endet.
    [Test]
    public async Task GetEventsByProcessInstance_ShouldFilterAndRetainCompletedTaskAudit()
    {
        using var context = new Context();
        var instanceId = Guid.NewGuid();
        var matchingTask = await context.AddTask(instanceId);
        var otherTask = await context.AddTask(Guid.NewGuid());
        var matching = Create(matchingTask, "claim", revision: 1);
        var other = Create(otherTask, "claim", revision: 1);
        await context.Storage.UserTaskLifecycleStorage.TryWrite(matching.State, 0, matching.Event);
        await context.Storage.UserTaskLifecycleStorage.TryWrite(other.State, 0, other.Event);

        await context.Storage.SubscriptionStorage.RemoveUserTaskSubscription(matchingTask.Id);

        var events = await context.Storage.UserTaskLifecycleStorage.GetEventsByProcessInstance(instanceId);

        events.Should().ContainSingle().Which.Id.Should().Be(matching.Event.Id);
    }

    internal static (UserTaskWorkState State, UserTaskAssignmentEvent Event) Create(
        UserTaskSubscription task, string action, long revision, AuthenticatedActor? authenticatedActor = null)
    {
        var now = DateTimeOffset.UtcNow;
        var state = new UserTaskWorkState
        {
            UserTaskId = task.Id,
            Revision = revision,
            AssigneeOwnerKey = new string('a', 64),
            AssigneeUserId = Guid.NewGuid(),
            AssigneeDisplayName = "Testperson",
            UpdatedAtUtc = now
        };
        return (state, new UserTaskAssignmentEvent
        {
            Id = Guid.NewGuid(),
            UserTaskId = task.Id,
            ProcessInstanceId = task.ProcessInstanceId,
            DefinitionId = task.DefinitionId,
            ProcessId = task.ProcessId,
            FlowNodeId = task.Token.CurrentFlowNode!.Id,
            Revision = revision,
            Action = action,
            ActorOwnerKey = new string('b', 64),
            ActorUserId = authenticatedActor?.UserId ?? Guid.NewGuid(),
            AuthenticatedActor = authenticatedActor,
            ActorDisplayName = "Testakteur",
            Reason = "Testgrund",
            CorrelationId = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = now
        });
    }

    private sealed class Context : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flowzer-task-lifecycle", Guid.NewGuid().ToString("N"));
        public Context()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            Storage = new Storage();
        }
        public Storage Storage { get; }
        public async Task<UserTaskSubscription> AddTask(Guid? processInstanceId = null)
        {
            var instanceId = processInstanceId ?? Guid.NewGuid();
            var task = new UserTask { Id = "Review", Name = "Review", Implementation = "ReviewForm" };
            var subscription = new UserTaskSubscription
            {
                Id = Guid.NewGuid(),
                Name = "Review",
                Token = new Token
                {
                    ProcessInstanceId = instanceId,
                    CurrentBaseElement = task,
                    ActiveBoundaryEvents = [],
                    State = FlowNodeState.Active
                },
                ProcessInstanceId = instanceId,
                MetaDefinitionId = "catalog",
                DefinitionId = Guid.NewGuid(),
                ProcessId = "Process"
            };
            await Storage.SubscriptionStorage.AddUserTaskSubscription(subscription);
            return subscription;
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previous);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
