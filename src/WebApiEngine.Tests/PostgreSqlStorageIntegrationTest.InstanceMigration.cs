using System.Dynamic;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Der Kernfall des Umzugs gilt auch auf dem Betriebspfad PostgreSQL — dort
    // laeuft er in einer echten Transaktion je Instanz.
    [Test]
    public async Task Migration_ShouldLiftAWaitingInstanceOnPostgreSql()
    {
        await InstanceMigrationScenarios.CoreAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Auch in PostgreSQL entscheidet die Formularbindung ueber den privaten Entwurf;
    // Spalte und Rumpf muessen dabei gemeinsam umgebunden werden.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Migration_ShouldKeepDraftsOnlyForIdenticalFormsOnPostgreSql(bool changeForm)
    {
        await InstanceMigrationScenarios.DraftAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema), changeForm);
    }

    // Testzweck: Eine gescheiterte Instanz rollt in PostgreSQL nur ihre eigene Transaktion
    // zurueck; die migrierbare Instanz derselben Anfrage bleibt migriert.
    [Test]
    public async Task Migration_ShouldMigratePartiallyOnPostgreSql()
    {
        await InstanceMigrationScenarios.PartialAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: In PostgreSQL liegt der Vergabezustand eines Auftrags in eigenen Spalten. Das
    // Umbinden auf die Zielversion darf die Sperre des arbeitenden Workers nicht loeschen.
    [Test]
    public async Task Migration_ShouldKeepLockedServiceTaskJobsOnPostgreSql()
    {
        await InstanceMigrationScenarios.ServiceTaskAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Timer- und Nachrichtenanmeldungen tragen auch auf dem Betriebspfad nach dem
    // Umzug die Zielversion.
    [Test]
    public async Task Migration_ShouldRebindSubscriptionsOnPostgreSql()
    {
        await InstanceMigrationScenarios.SubscriptionsAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Ein Entwurf, der waehrend des Umzugs gespeichert wird, darf die Aufgabe nicht
    // an die Quellversion zurueckbinden — weder als Ersatz eines bestehenden Entwurfs noch als
    // neu angelegter. Nimmt der Umzug die Aufgabensperre nicht, liest der Speichervorgang die
    // alte Bindung und der naechste Abruf scheitert an der Bindungspruefung (HTTP 500).
    [TestCase(true)]
    [TestCase(false)]
    public async Task Migration_ShouldHoldTheTaskLockAgainstAParallelDraftSaveOnPostgreSql(bool draftExists)
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var engine = new BpmnBusinessLogic(provider);
        await InstanceMigrationScenarios.DeployAsync(provider, engine, new Model.Version(1, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: false));
        var instance = await InstanceMigrationScenarios.StartAsync(engine, "left");
        var task = (await InstanceMigrationScenarios.TasksAsync(provider, instance.InstanceId)).Single();
        var user = new CurrentUserContext(Guid.NewGuid(), "migration-draft-test", false);
        var ownerKey = UserTaskDraftOwnerKey.Create(user);
        if (draftExists) await SaveDraftAsync(provider, task, ownerKey, user.UserId);

        // Gleiches Formular in der Zielversion: Der Entwurf wird umgebunden statt verworfen.
        var target = await InstanceMigrationScenarios.DeployAsync(provider, engine, new Model.Version(2, 0),
            InstanceMigrationScenarios.Xml(InstanceMigrationScenarios.FirstForm, withApprove: true));

        var taskLocked = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var hooks = new MigrationStorageHooks
        {
            // Der Griff sitzt auf der Aufgabensperre selbst: Nimmt die Migration sie nicht,
            // laeuft er nie und der Test scheitert an dieser Wartezeit.
            AfterTaskLock = async () =>
            {
                taskLocked.TrySetResult();
                await release.Task;
            }
        };
        var migration = new BpmnBusinessLogic(new HookedTransactionalStorageProvider(provider, hooks))
            .MigrateInstances([instance.InstanceId], target.Id, user.UserId);
        await taskLocked.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var drafts = CreateDraftService(provider, user);
        var save = drafts.SaveAsync(task.Id, new SaveUserTaskDraftRequestDto
        {
            ExpectedRevision = draftExists ? 1 : 0,
            Data = Data("waehrend der Migration")
        });
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        save.IsCompleted.Should().BeFalse("das Speichern muss auf die Aufgabensperre der Migration warten");

        release.SetResult();
        (await migration).Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();
        (await save)!.Revision.Should().Be(draftExists ? 2 : 1);

        // Der Entwurf bleibt ueber den Anwendungsfall lesbar; eine Bindung an die Quellversion
        // wuerde hier mit InvalidDataException scheitern.
        (await drafts.GetAsync(task.Id))!.Revision.Should().Be(draftExists ? 2 : 1);
        using var reader = provider.GetTransactionalStorage();
        (await reader.UserTaskDraftStorage.Get(task.Id, ownerKey))!.DefinitionId.Should().Be(target.Id);
    }

    private static async Task SaveDraftAsync(
        ITransactionalStorageProvider provider,
        UserTaskSubscription task,
        string ownerKey,
        Guid ownerUserId)
    {
        using var storage = provider.GetTransactionalStorage();
        (await storage.UserTaskDraftStorage.TrySave(new UserTaskDraft
        {
            UserTaskId = task.Id,
            OwnerKey = ownerKey,
            OwnerUserId = ownerUserId,
            TokenId = task.Token.Id,
            ProcessInstanceId = task.ProcessInstanceId!.Value,
            DefinitionId = task.DefinitionId,
            Revision = 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            DataJson = """{"answer":"vor der Migration"}"""
        }, expectedRevision: 0)).Status.Should().Be(UserTaskDraftWriteStatus.Written);
        storage.CommitChanges();
    }

    private static ExpandoObject Data(string answer)
    {
        dynamic data = new ExpandoObject();
        data.answer = answer;
        return (ExpandoObject)data;
    }

    /// <summary>
    /// Der echte Anwendungsfall ueber der echten Ablage; nur Identitaet und Betriebsrecht
    /// stammen aus dem Testkontext statt aus einem HTTP-Request.
    /// </summary>
    private static UserTaskDraftService CreateDraftService(
        ITransactionalStorageProvider provider,
        CurrentUserContext user) => new(
        provider,
        new FixedCurrentUserContextAccessor(user),
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
        new AlwaysAuthorizedService(),
        TimeProvider.System);

    private sealed class FixedCurrentUserContextAccessor(CurrentUserContext user) : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser() => user;
    }

    private sealed class AlwaysAuthorizedService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            System.Security.Claims.ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            System.Security.Claims.ClaimsPrincipal user,
            object? resource,
            string policyName) => Task.FromResult(AuthorizationResult.Success());
    }
}
