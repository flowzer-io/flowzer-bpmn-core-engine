using BPMN.Common;
using BPMN.HumanInteraction;
using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>
/// Belegt die Revisions- und Lebenszyklusregeln der Entwicklungsablage. Die Dateiablage ist
/// bewusst nur innerhalb eines Prozesses konkurrenzsicher; produktiver Mehrprozessbetrieb
/// verwendet PostgreSQL.
/// </summary>
[NonParallelizable]
public sealed class UserTaskDraftStorageTest
{
    // Testzweck: Zwei Browser-Tabs mit derselben Ausgangsrevision duerfen nicht beide
    // erfolgreich schreiben; genau der Gewinner bleibt als Revision 1 lesbar.
    [Test]
    public async Task ConcurrentCreates_ShouldPersistExactlyOneWinner()
    {
        using var context = new StorageContext();
        var subscription = await context.AddUserTaskAsync();
        var ownerKey = new string('a', 64);
        var first = CreateDraft(subscription, ownerKey, "erste Eingabe");
        var second = CreateDraft(subscription, ownerKey, "zweite Eingabe");

        var results = await Task.WhenAll(
            context.Storage.UserTaskDraftStorage.TrySave(first, expectedRevision: 0),
            context.Storage.UserTaskDraftStorage.TrySave(second, expectedRevision: 0));

        results.Should().ContainSingle(result => result.Status == UserTaskDraftWriteStatus.Written);
        results.Should().ContainSingle(result => result.Status == UserTaskDraftWriteStatus.RevisionConflict);
        var winner = results.Single(result => result.Status == UserTaskDraftWriteStatus.Written).Draft!;
        (await context.Storage.UserTaskDraftStorage.Get(subscription.Id, ownerKey))
            .Should().BeEquivalentTo(winner);
    }

    // Testzweck: Wird eine User-Task entfernt, verschwinden alle privaten Entwuerfe dieser
    // Aufgabe, ohne dass die Entwuerfe anderer Aufgaben betroffen sind.
    [Test]
    public async Task RemovingUserTask_ShouldDeleteOnlyItsDrafts()
    {
        using var context = new StorageContext();
        var removedTask = await context.AddUserTaskAsync();
        var remainingTask = await context.AddUserTaskAsync();
        var firstOwner = new string('b', 64);
        var secondOwner = new string('c', 64);
        await context.Storage.UserTaskDraftStorage.TrySave(
            CreateDraft(removedTask, firstOwner, "eins"), expectedRevision: 0);
        await context.Storage.UserTaskDraftStorage.TrySave(
            CreateDraft(removedTask, secondOwner, "zwei"), expectedRevision: 0);
        await context.Storage.UserTaskDraftStorage.TrySave(
            CreateDraft(remainingTask, firstOwner, "bleibt"), expectedRevision: 0);

        await context.Storage.SubscriptionStorage.RemoveUserTaskSubscription(removedTask.Id);

        (await context.Storage.UserTaskDraftStorage.Get(removedTask.Id, firstOwner)).Should().BeNull();
        (await context.Storage.UserTaskDraftStorage.Get(removedTask.Id, secondOwner)).Should().BeNull();
        (await context.Storage.UserTaskDraftStorage.Get(remainingTask.Id, firstOwner)).Should().NotBeNull();
    }

    private static UserTaskDraft CreateDraft(
        UserTaskSubscription subscription,
        string ownerKey,
        string value) => new()
        {
            UserTaskId = subscription.Id,
            OwnerKey = ownerKey,
            OwnerUserId = Guid.NewGuid(),
            TokenId = subscription.Token.Id,
            ProcessInstanceId = subscription.Token.ProcessInstanceId,
            DefinitionId = subscription.DefinitionId,
            Revision = 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            DataJson = $$"""{"value":"{{value}}"}"""
        };

    private sealed class StorageContext : IDisposable
    {
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;

        public StorageContext()
        {
            _previousStorageRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(
                Path.GetTempPath(), "flowzer-user-task-draft-storage-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _storageRoot);
            Storage = new Storage();
        }

        public Storage Storage { get; }

        public async Task<UserTaskSubscription> AddUserTaskAsync()
        {
            var instanceId = Guid.NewGuid();
            var userTask = new UserTask
            {
                Id = "UserTask_Draft",
                Name = "Entwurf pruefen",
                Implementation = "DraftForm"
            };
            var subscription = new UserTaskSubscription
            {
                Id = Guid.NewGuid(),
                Name = userTask.Name,
                Token = new Token
                {
                    ProcessInstanceId = instanceId,
                    CurrentBaseElement = userTask,
                    ActiveBoundaryEvents = [],
                    State = FlowNodeState.Active
                },
                ProcessInstanceId = instanceId,
                MetaDefinitionId = "draft-catalog",
                DefinitionId = Guid.NewGuid(),
                ProcessId = "Process_Draft"
            };
            await Storage.SubscriptionStorage.AddUserTaskSubscription(subscription);
            return subscription;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
