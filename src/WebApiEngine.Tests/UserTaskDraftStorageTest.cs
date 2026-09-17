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

    // Testzweck: Die Instanzmigration muss vor dem Umzug sagen koennen, wie viele fremde
    // Entwuerfe sie verwirft — gezaehlt ueber alle Eigentuemer, aber nur dieser einen Aufgabe.
    [Test]
    public async Task CountAndDeleteForTask_ShouldCoverAllOwnersOfExactlyThatTask()
    {
        using var context = new StorageContext();
        var task = await context.AddUserTaskAsync();
        var otherTask = await context.AddUserTaskAsync();
        var firstOwner = new string('1', 64);
        var secondOwner = new string('2', 64);
        await context.Storage.UserTaskDraftStorage.TrySave(CreateDraft(task, firstOwner, "eins"), 0);
        await context.Storage.UserTaskDraftStorage.TrySave(CreateDraft(task, secondOwner, "zwei"), 0);
        await context.Storage.UserTaskDraftStorage.TrySave(CreateDraft(otherTask, firstOwner, "fremd"), 0);

        (await context.Storage.UserTaskDraftStorage.CountForTask(task.Id)).Should().Be(2);

        (await context.Storage.UserTaskDraftStorage.DeleteAllForTask(task.Id)).Should().Be(2);
        (await context.Storage.UserTaskDraftStorage.CountForTask(task.Id)).Should().Be(0);
        (await context.Storage.UserTaskDraftStorage.CountForTask(otherTask.Id)).Should().Be(1);
    }

    // Testzweck: Beim Umbinden auf die Zielversion bleibt der Entwurf samt Revision lesbar;
    // eine hochgezaehlte Revision machte den offenen Browser-Tab des Bearbeiters zum Konflikt.
    [Test]
    public async Task RebindAllForTask_ShouldChangeOnlyTheDefinitionBinding()
    {
        using var context = new StorageContext();
        var task = await context.AddUserTaskAsync();
        var ownerKey = new string('3', 64);
        var draft = CreateDraft(task, ownerKey, "bleibt");
        await context.Storage.UserTaskDraftStorage.TrySave(draft, 0);
        var target = Guid.NewGuid();

        (await context.Storage.UserTaskDraftStorage.RebindAllForTask(task.Id, target)).Should().Be(1);

        var stored = (await context.Storage.UserTaskDraftStorage.Get(task.Id, ownerKey))!;
        stored.DefinitionId.Should().Be(target);
        stored.Revision.Should().Be(draft.Revision);
        stored.DataJson.Should().Be(draft.DataJson);
        stored.TokenId.Should().Be(draft.TokenId);
        stored.UpdatedAtUtc.Should().Be(draft.UpdatedAtUtc);
    }

    // Testzweck: Zwischen dem Lesen eines Entwurfs und seinem Umbinden darf nichts verloren
    // gehen. Liest das Umbinden ausserhalb der Entwurfssperre, schreibt es den Stand von vor
    // dem letzten Speichern zurueck — der Bearbeiter verlaere seine Eingabe unbemerkt.
    [Test]
    public async Task RebindAllForTask_ShouldNotOverwriteASaveThatLandedBeforeTheLockWasGranted()
    {
        using var context = new StorageContext();
        var task = await context.AddUserTaskAsync();
        var ownerKey = new string('4', 64);
        await context.Storage.UserTaskDraftStorage.TrySave(CreateDraft(task, ownerKey, "alt"), 0);
        var target = Guid.NewGuid();

        var gate = UserTaskDraftStorage.Locks.GetOrAdd(
            UserTaskDraftStorage.Key(task.Id, ownerKey), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        var rebind = context.Storage.UserTaskDraftStorage.RebindAllForTask(task.Id, target);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        rebind.IsCompleted.Should().BeFalse("das Umbinden muss auf die Entwurfssperre warten");

        // Der Stand, den ein Speichervorgang hinterlassen hat, waehrend das Umbinden wartete.
        await context.WriteDraftFileAsync(CreateDraft(task, ownerKey, "neu", revision: 2));
        gate.Release();
        (await rebind).Should().Be(1);

        var stored = (await context.Storage.UserTaskDraftStorage.Get(task.Id, ownerKey))!;
        stored.DataJson.Should().Be("""{"value":"neu"}""");
        stored.Revision.Should().Be(2);
        stored.DefinitionId.Should().Be(target);
    }

    // Testzweck: Auch das Verwerfen aller Entwuerfe einer Aufgabe gehoert unter die
    // Entwurfssperre; sonst koennte ein gleichzeitiges Speichern den verworfenen Entwurf
    // wiederauferstehen lassen.
    [Test]
    public async Task DeleteAllForTask_ShouldWaitForTheDraftLock()
    {
        using var context = new StorageContext();
        var task = await context.AddUserTaskAsync();
        var ownerKey = new string('5', 64);
        await context.Storage.UserTaskDraftStorage.TrySave(CreateDraft(task, ownerKey, "weg"), 0);

        var gate = UserTaskDraftStorage.Locks.GetOrAdd(
            UserTaskDraftStorage.Key(task.Id, ownerKey), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        var delete = context.Storage.UserTaskDraftStorage.DeleteAllForTask(task.Id);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        delete.IsCompleted.Should().BeFalse("das Verwerfen muss auf die Entwurfssperre warten");

        gate.Release();
        (await delete).Should().Be(1);
        (await context.Storage.UserTaskDraftStorage.Get(task.Id, ownerKey)).Should().BeNull();
    }

    private static UserTaskDraft CreateDraft(
        UserTaskSubscription subscription,
        string ownerKey,
        string value,
        long revision = 1) => new()
        {
            UserTaskId = subscription.Id,
            OwnerKey = ownerKey,
            OwnerUserId = Guid.NewGuid(),
            TokenId = subscription.Token.Id,
            ProcessInstanceId = subscription.Token.ProcessInstanceId,
            DefinitionId = subscription.DefinitionId,
            Revision = revision,
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

        /// <summary>
        /// Legt eine Entwurfsdatei unter Umgehung der Ablage ab. Damit laesst sich ein
        /// Speichervorgang nachstellen, der genau waehrend eines anderen Zugriffs landet.
        /// </summary>
        public async Task WriteDraftFileAsync(UserTaskDraft draft)
        {
            var path = Path.Combine(
                Storage.GetBasePath(Path.Combine("FileStorage", UserTaskDraftStorage.DirectoryName)),
                $"draft_{draft.UserTaskId:N}_{draft.OwnerKey}.json");
            await File.WriteAllTextAsync(
                path, Newtonsoft.Json.JsonConvert.SerializeObject(draft, Storage.NewtonSoftDefaultSettings));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
