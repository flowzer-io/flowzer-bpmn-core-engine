using FluentAssertions;
using PostgreSqlStorageSystem;
using StorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: PostgreSQL entscheidet einen konkurrierenden Erst-Claim über zwei Sessions
    // atomar und koppelt genau ein Auditereignis an den Gewinner.
    [Test]
    public async Task UserTaskLifecycle_ShouldCompareAndSwapAcrossSessions()
    {
        using var first = new PostgreSqlStorage(_dataSource!, Schema);
        using var second = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(first);
        var left = UserTaskLifecycleStorageTest.Create(task, "claim", revision: 1);
        var right = UserTaskLifecycleStorageTest.Create(task, "claim", revision: 1);

        var results = await Task.WhenAll(
            first.UserTaskLifecycleStorage.TryWrite(left.State, 0, left.Event),
            second.UserTaskLifecycleStorage.TryWrite(right.State, 0, right.Event));

        results.Should().ContainSingle(result => result.Status == UserTaskLifecycleWriteStatus.Written);
        results.Should().ContainSingle(result => result.Status == UserTaskLifecycleWriteStatus.RevisionConflict);
        (await first.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().ContainSingle();
    }

    // Testzweck: Der Fremdschlüssel entfernt den offenen Zustand beim Taskende, während die
    // Auditspur ohne löschenden Fremdschlüssel erhalten bleibt.
    [Test]
    public async Task UserTaskLifecycle_ShouldCascadeStateButRetainAudit()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(storage);
        var item = UserTaskLifecycleStorageTest.Create(task, "claim", revision: 1);
        (await storage.UserTaskLifecycleStorage.TryWrite(item.State, 0, item.Event)).Status
            .Should().Be(UserTaskLifecycleWriteStatus.Written);

        await storage.SubscriptionStorage.RemoveUserTaskSubscription(task.Id);

        (await storage.UserTaskLifecycleStorage.Get(task.Id)).Should().BeNull();
        (await storage.UserTaskLifecycleStorage.GetEvents(task.Id)).Should().ContainSingle();
        (await storage.UserTaskLifecycleStorage.TryWrite(item.State, 0, item.Event)).Status
            .Should().Be(UserTaskLifecycleWriteStatus.TaskNotFound);
    }

    // Testzweck: Die PostgreSQL-Vorgangsabfrage filtert über die persistierte Instanzkennung
    // und lässt die Append-only-Spur nach dem Ende der offenen Subscription erreichbar.
    [Test]
    public async Task UserTaskLifecycle_ShouldQueryRetainedAuditByProcessInstance()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var instanceId = Guid.NewGuid();
        var matchingTask = await AddDraftUserTaskAsync(storage, instanceId);
        var otherTask = await AddDraftUserTaskAsync(storage, Guid.NewGuid());
        var matching = UserTaskLifecycleStorageTest.Create(matchingTask, "claim", revision: 1);
        var other = UserTaskLifecycleStorageTest.Create(otherTask, "claim", revision: 1);
        (await storage.UserTaskLifecycleStorage.TryWrite(matching.State, 0, matching.Event)).Status
            .Should().Be(UserTaskLifecycleWriteStatus.Written);
        (await storage.UserTaskLifecycleStorage.TryWrite(other.State, 0, other.Event)).Status
            .Should().Be(UserTaskLifecycleWriteStatus.Written);

        await storage.SubscriptionStorage.RemoveUserTaskSubscription(matchingTask.Id);

        var events = await storage.UserTaskLifecycleStorage.GetEventsByProcessInstance(instanceId);

        events.Should().ContainSingle().Which.Id.Should().Be(matching.Event.Id);
    }
}
