using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Zwei PostgreSQL-Sessions können dieselbe Deadline-Revision und denselben
    // Meldungs-Meilenstein jeweils nur einmal erfolgreich persistieren.
    [Test]
    public async Task UserTaskDeadlines_ShouldCompareAndDeduplicateAcrossSessions()
    {
        using var first = new PostgreSqlStorage(_dataSource!, Schema);
        using var second = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(first);
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var deadline = Deadline(task.Id, 1, now);
        await first.UserTaskDeadlineStorage.AddIfAbsent(deadline);
        var advanced = deadline with
        {
            Revision = 2,
            Status = "overdue",
            EmittedMilestones = ["due"],
            NextCheckAtUtc = null,
            UpdatedAtUtc = now
        };

        var advances = await Task.WhenAll(
            first.UserTaskDeadlineStorage.TryAdvance(advanced, 1),
            second.UserTaskDeadlineStorage.TryAdvance(advanced, 1));
        advances.Should().ContainSingle(value => value);
        advances.Should().ContainSingle(value => !value);

        var notification = Notification(task.Id, now);
        var writes = await Task.WhenAll(
            first.UserTaskNotificationStorage.TryAdd(notification),
            second.UserTaskNotificationStorage.TryAdd(notification with { Id = Guid.NewGuid() }));
        writes.Should().ContainSingle(result => result.Added);
        writes.Should().ContainSingle(result => !result.Added);
    }

    // Testzweck: PostgreSQL hält Lesestatus je Person getrennt und entfernt Deadline,
    // Meldung sowie Quittierungen atomar mit der Task-Subscription.
    [Test]
    public async Task UserTaskDeadlines_ShouldCascadeWithTaskAndSeparateReadOwners()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(storage);
        var now = DateTimeOffset.UtcNow;
        await storage.UserTaskDeadlineStorage.AddIfAbsent(Deadline(task.Id, 1, now));
        var written = await storage.UserTaskNotificationStorage.TryAdd(Notification(task.Id, now));
        var notificationId = written.Notification!.Id;
        var firstOwner = new string('a', 64);
        var secondOwner = new string('b', 64);
        (await storage.UserTaskNotificationStorage.MarkRead(notificationId, firstOwner, now)).Should().BeTrue();

        (await storage.UserTaskNotificationStorage.GetForTasks(
            [task.Id], firstOwner, null, 20, false)).Single().ReadAtUtc.Should()
            // PostgreSQL speichert timestamptz auf Mikrosekunden genau, DateTimeOffset
            // kann lokal dagegen noch eine zusätzliche 100-ns-Stelle tragen.
            .BeCloseTo(now, TimeSpan.FromTicks(9));
        (await storage.UserTaskNotificationStorage.GetForTasks(
            [task.Id], secondOwner, null, 20, false)).Single().ReadAtUtc.Should().BeNull();

        await storage.SubscriptionStorage.RemoveUserTaskSubscription(task.Id);

        (await storage.UserTaskDeadlineStorage.Get(task.Id)).Should().BeNull();
        (await storage.UserTaskNotificationStorage.Get(notificationId)).Should().BeNull();
        (await storage.UserTaskNotificationStorage.MarkRead(notificationId, secondOwner, now)).Should().BeFalse();
    }

    private static UserTaskDeadline Deadline(Guid taskId, long revision, DateTimeOffset now) => new()
    {
        UserTaskId = taskId,
        Revision = revision,
        ActivatedAtUtc = now.AddHours(-1),
        RawDueDate = "PT1H",
        ScheduleState = "resolved",
        Status = "scheduled",
        DueAtUtc = now,
        NextCheckAtUtc = now,
        PolicyVersion = "test-v1",
        UpdatedAtUtc = now.AddHours(-1)
    };

    private static UserTaskNotification Notification(Guid taskId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        UserTaskId = taskId,
        Kind = "due",
        DeduplicationKey = $"task:{taskId:N}:test-v1:due:{now:O}",
        OccurredAtUtc = now
    };
}
