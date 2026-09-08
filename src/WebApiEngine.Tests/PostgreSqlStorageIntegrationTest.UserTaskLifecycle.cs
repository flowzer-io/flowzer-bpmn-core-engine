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
}
