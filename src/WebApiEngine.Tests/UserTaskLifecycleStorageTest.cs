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

    internal static (UserTaskWorkState State, UserTaskAssignmentEvent Event) Create(
        UserTaskSubscription task, string action, long revision)
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
            ActorUserId = Guid.NewGuid(),
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
        public async Task<UserTaskSubscription> AddTask()
        {
            var task = new UserTask { Id = "Review", Name = "Review", Implementation = "ReviewForm" };
            var subscription = new UserTaskSubscription
            {
                Id = Guid.NewGuid(),
                Name = "Review",
                Token = new Token
                {
                    ProcessInstanceId = Guid.NewGuid(),
                    CurrentBaseElement = task,
                    ActiveBoundaryEvents = [],
                    State = FlowNodeState.Active
                },
                ProcessInstanceId = Guid.NewGuid(),
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
