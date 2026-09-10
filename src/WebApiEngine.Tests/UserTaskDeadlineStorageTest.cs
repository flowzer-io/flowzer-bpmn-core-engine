using BPMN.HumanInteraction;
using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>Belegt Bindung, Fortschritt und Deduplizierung der Entwicklungsablage.</summary>
[NonParallelizable]
public sealed class UserTaskDeadlineStorageTest
{
    // Testzweck: Der erste gebundene Termin bleibt erhalten, wenn dieselbe stabile
    // Task-ID bei einem späteren Engine-Speicherlauf erneut erscheint.
    [Test]
    public async Task AddIfAbsent_ShouldKeepFirstBoundSchedule()
    {
        using var context = new Context();
        var task = await context.AddTask();
        var first = Deadline(task.Id, revision: 1, dueAt: context.Now.AddHours(2));
        var shifted = Deadline(task.Id, revision: 1, dueAt: context.Now.AddDays(2)) with
        {
            PolicyVersion = "changed-v2"
        };

        var inserted = await context.Storage.UserTaskDeadlineStorage.AddIfAbsent(first);
        var existing = await context.Storage.UserTaskDeadlineStorage.AddIfAbsent(shifted);

        inserted.Should().BeEquivalentTo(first);
        existing.DueAtUtc.Should().Be(first.DueAtUtc);
    }

    // Testzweck: Eine Deadline-Revision kann nur einmal fortgeschrieben werden, damit
    // zwei Schedulerläufe keine Meilensteine verlieren.
    [Test]
    public async Task TryAdvance_ShouldAllowOnlyOneWriterPerRevision()
    {
        using var context = new Context();
        var task = await context.AddTask();
        await context.Storage.UserTaskDeadlineStorage.AddIfAbsent(
            Deadline(task.Id, revision: 1, dueAt: context.Now));
        var advanced = Deadline(task.Id, revision: 2, dueAt: context.Now) with
        {
            EmittedMilestones = ["due"],
            NextCheckAtUtc = null
        };

        var results = await Task.WhenAll(
            context.Storage.UserTaskDeadlineStorage.TryAdvance(advanced, expectedRevision: 1),
            context.Storage.UserTaskDeadlineStorage.TryAdvance(advanced, expectedRevision: 1));

        results.Should().ContainSingle(value => value);
        results.Should().ContainSingle(value => !value);
    }

    // Testzweck: Derselbe fachliche Meilenstein erzeugt auch bei Wiederholung nur eine
    // Meldung, während Lesequittierungen je Person getrennt bleiben.
    [Test]
    public async Task Notifications_ShouldDeduplicateAndTrackReadsPerOwner()
    {
        using var context = new Context();
        var task = await context.AddTask();
        var notification = Notification(task.Id, "due", context.Now);

        var first = await context.Storage.UserTaskNotificationStorage.TryAdd(notification);
        var replay = await context.Storage.UserTaskNotificationStorage.TryAdd(
            notification with { Id = Guid.NewGuid() });
        await context.Storage.UserTaskNotificationStorage.MarkRead(first.Notification!.Id, new string('a', 64), context.Now);
        await context.Storage.UserTaskNotificationStorage.MarkRead(
            first.Notification.Id, new string('a', 64), context.Now.AddHours(1));

        first.Added.Should().BeTrue();
        replay.Added.Should().BeFalse();
        (await context.Storage.UserTaskNotificationStorage.GetForTasks(
            [task.Id], new string('a', 64), null, 20, unreadOnly: false)).Should().ContainSingle()
            .Which.ReadAtUtc.Should().Be(context.Now);
        (await context.Storage.UserTaskNotificationStorage.GetForTasks(
            [task.Id], new string('b', 64), null, 20, unreadOnly: false)).Should().ContainSingle()
            .Which.ReadAtUtc.Should().BeNull();
    }

    private static UserTaskDeadline Deadline(Guid taskId, long revision, DateTimeOffset dueAt) => new()
    {
        UserTaskId = taskId,
        Revision = revision,
        ActivatedAtUtc = dueAt.AddHours(-1),
        RawDueDate = "PT1H",
        ScheduleState = "resolved",
        Status = "scheduled",
        DueAtUtc = dueAt,
        NextCheckAtUtc = dueAt,
        PolicyVersion = "test-v1",
        UpdatedAtUtc = dueAt.AddHours(-1)
    };

    private static UserTaskNotification Notification(Guid taskId, string kind, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        UserTaskId = taskId,
        Kind = kind,
        DeduplicationKey = $"task:{taskId:N}:{kind}:{at:O}",
        OccurredAtUtc = at
    };

    private sealed class Context : IDisposable
    {
        private readonly string? _previous = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flowzer-task-deadline", Guid.NewGuid().ToString("N"));
        public Context()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            Storage = new Storage();
        }

        public DateTimeOffset Now { get; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public Storage Storage { get; }

        public async Task<UserTaskSubscription> AddTask()
        {
            var node = new UserTask { Id = "Review", Name = "Review", Implementation = "ReviewForm" };
            var subscription = new UserTaskSubscription
            {
                Id = Guid.NewGuid(),
                Name = "Review",
                Token = new Token
                {
                    ProcessInstanceId = Guid.NewGuid(), CurrentBaseElement = node,
                    ActiveBoundaryEvents = [], State = FlowNodeState.Active
                },
                ProcessInstanceId = Guid.NewGuid(), MetaDefinitionId = "catalog",
                DefinitionId = Guid.NewGuid(), ProcessId = "Process"
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
