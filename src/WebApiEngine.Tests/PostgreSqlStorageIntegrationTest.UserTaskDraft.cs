using BPMN.HumanInteraction;
using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Das bedingte PostgreSQL-Statement entscheidet auch zwischen unabhaengigen
    // Sessions atomar; exakt ein konkurrierender Erstersteller gewinnt.
    [Test]
    public async Task UserTaskDraftStorage_ShouldCompareAndSwapAcrossSessions()
    {
        using var firstStorage = new PostgreSqlStorage(_dataSource!, Schema);
        using var secondStorage = new PostgreSqlStorage(_dataSource!, Schema);
        var subscription = await AddDraftUserTaskAsync(firstStorage);
        var ownerKey = new string('d', 64);
        var first = CreateDraft(subscription, ownerKey, "erste Eingabe", revision: 1);
        var second = CreateDraft(subscription, ownerKey, "zweite Eingabe", revision: 1);

        var createResults = await Task.WhenAll(
            firstStorage.UserTaskDraftStorage.TrySave(first, expectedRevision: 0),
            secondStorage.UserTaskDraftStorage.TrySave(second, expectedRevision: 0));

        createResults.Should().ContainSingle(result => result.Status == UserTaskDraftWriteStatus.Written);
        createResults.Should().ContainSingle(result => result.Status == UserTaskDraftWriteStatus.RevisionConflict);
        var winner = createResults.Single(result => result.Status == UserTaskDraftWriteStatus.Written).Draft!;
        (await firstStorage.UserTaskDraftStorage.Get(subscription.Id, ownerKey))
            .Should().BeEquivalentTo(winner);

        var updated = CreateDraft(subscription, ownerKey, "aktualisiert", revision: 2);
        var updateResult = await secondStorage.UserTaskDraftStorage.TrySave(updated, expectedRevision: 1);
        updateResult.Status.Should().Be(UserTaskDraftWriteStatus.Written);
        updateResult.CurrentRevision.Should().Be(2);

        var staleDelete = await firstStorage.UserTaskDraftStorage.TryDelete(
            subscription.Id, ownerKey, expectedRevision: 1);
        staleDelete.Status.Should().Be(UserTaskDraftDeleteStatus.RevisionConflict);
        staleDelete.CurrentRevision.Should().Be(2);

        var delete = await firstStorage.UserTaskDraftStorage.TryDelete(
            subscription.Id, ownerKey, expectedRevision: 2);
        delete.Status.Should().Be(UserTaskDraftDeleteStatus.Deleted);
        (await secondStorage.UserTaskDraftStorage.Get(subscription.Id, ownerKey)).Should().BeNull();
    }

    // Testzweck: Das Entfernen der Aufgaben-Subscription beseitigt deren Entwuerfe per
    // Fremdschluessel atomar; ein spaeter Schreibversuch meldet die verschwundene Aufgabe.
    [Test]
    public async Task UserTaskDraftStorage_ShouldCascadeWithSubscriptionLifecycle()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var subscription = await AddDraftUserTaskAsync(storage);
        var ownerKey = new string('e', 64);
        var draft = CreateDraft(subscription, ownerKey, "zu bereinigen", revision: 1);
        (await storage.UserTaskDraftStorage.TrySave(draft, expectedRevision: 0)).Status
            .Should().Be(UserTaskDraftWriteStatus.Written);

        await storage.SubscriptionStorage.RemoveUserTaskSubscription(subscription.Id);

        (await storage.UserTaskDraftStorage.Get(subscription.Id, ownerKey)).Should().BeNull();
        (await storage.UserTaskDraftStorage.TrySave(draft, expectedRevision: 0)).Status
            .Should().Be(UserTaskDraftWriteStatus.TaskNotFound);
    }

    // Testzweck: Zaehlen, Loeschen und Umbinden je Aufgabe wirken in PostgreSQL genau auf deren
    // Entwuerfe; das Umbinden aktualisiert Spalte und Rumpf gemeinsam und laesst die Revision.
    [Test]
    public async Task UserTaskDraftStorage_ShouldCountDeleteAndRebindPerTask()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var task = await AddDraftUserTaskAsync(storage);
        var otherTask = await AddDraftUserTaskAsync(storage);
        var firstOwner = new string('1', 64);
        var secondOwner = new string('2', 64);
        await storage.UserTaskDraftStorage.TrySave(CreateDraft(task, firstOwner, "eins", 1), 0);
        await storage.UserTaskDraftStorage.TrySave(CreateDraft(task, secondOwner, "zwei", 1), 0);
        await storage.UserTaskDraftStorage.TrySave(CreateDraft(otherTask, firstOwner, "fremd", 1), 0);

        (await storage.UserTaskDraftStorage.CountForTask(task.Id)).Should().Be(2);

        var target = Guid.NewGuid();
        (await storage.UserTaskDraftStorage.RebindAllForTask(task.Id, target)).Should().Be(2);
        var rebound = (await storage.UserTaskDraftStorage.Get(task.Id, firstOwner))!;
        rebound.DefinitionId.Should().Be(target);
        rebound.Revision.Should().Be(1);
        rebound.DataJson.Should().Be("""{"value":"eins"}""");
        (await storage.UserTaskDraftStorage.Get(otherTask.Id, firstOwner))!.DefinitionId
            .Should().Be(otherTask.DefinitionId);

        (await storage.UserTaskDraftStorage.DeleteAllForTask(task.Id)).Should().Be(2);
        (await storage.UserTaskDraftStorage.CountForTask(task.Id)).Should().Be(0);
        (await storage.UserTaskDraftStorage.CountForTask(otherTask.Id)).Should().Be(1);
    }

    private static async Task<UserTaskSubscription> AddDraftUserTaskAsync(
        PostgreSqlStorage storage, Guid? processInstanceId = null)
    {
        var instanceId = processInstanceId ?? Guid.NewGuid();
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
        await storage.SubscriptionStorage.AddUserTaskSubscription(subscription);
        return subscription;
    }

    private static UserTaskDraft CreateDraft(
        UserTaskSubscription subscription,
        string ownerKey,
        string value,
        long revision) => new()
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
}
