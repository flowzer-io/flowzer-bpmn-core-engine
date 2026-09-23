using System.Dynamic;
using core_engine;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>
/// Identische Migrationsfaelle fuer isolierte Datei- und PostgreSQL-Ablage. Die Szenarien
/// arbeiten ausschliesslich ueber die echte Geschaeftslogik und die echte Ablage; der
/// HTTP-Vertrag liegt in <see cref="InstanceMigrationIntegrationTest"/>.
/// </summary>
internal static class InstanceMigrationScenarios
{
    internal const string MetaDefinitionId = "Definitions_Migration";
    internal const string FirstForm = "Approval";
    internal const string SecondForm = "ApprovalV2";

    /// <summary>Die Zielversion kennt die Aufgabe "Review" nur noch unter dieser Kennung.</summary>
    internal const string RenamedReviewNodeId = "ReviewRenamed";

    /// <summary>Die Zielversion kennt den Service-Task "Fetch" nur noch unter dieser Kennung.</summary>
    internal const string RenamedFetchNodeId = "FetchRenamed";

    /// <summary>Der Name, den der zugeordnete Service-Task in der Zielversion traegt.</summary>
    internal const string RenamedFetchNodeName = "Daten holen";

    /// <summary>
    /// Der Kernfall: Eine wartende Aufgabe behaelt Kennung und Bearbeitungsdaten, die Instanz
    /// haengt danach an der Zielversion, folgt deren Sequenzfluessen — und die Migrationsspur
    /// ueberlebt den naechsten gewoehnlichen Schreibvorgang.
    /// </summary>
    internal static async Task CoreAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        var first = await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        var review = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        var assignee = Guid.NewGuid();
        using (var storage = provider.GetTransactionalStorage())
        {
            review.CurrenAssignedUser = assignee;
            review.Assignee = "bert";
            await storage.SubscriptionStorage.AddUserTaskSubscription(review);
            storage.CommitChanges();
        }

        var second = await DeployAsync(provider, engine, new Model.Version(2, 0), Xml(FirstForm, withApprove: true));

        var preview = await engine.PreviewInstanceMigration([instance.InstanceId]);
        preview.Status.Should().Be(InstanceMigrationRequestStatus.Accepted);
        preview.SourceDefinitionId.Should().Be(first.Id);
        preview.SourceVersion.Should().Be(new Model.Version(1, 0));
        preview.TargetDefinitionId.Should().Be(second.Id);
        preview.TargetVersion.Should().Be(new Model.Version(2, 0));
        preview.Instances.Should().ContainSingle().Which.Migratable.Should().BeTrue();

        var outcome = await engine.MigrateInstances([instance.InstanceId], second.Id, assignee);
        outcome.Status.Should().Be(InstanceMigrationRequestStatus.Accepted);
        outcome.Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();

        var migrated = await InstanceAsync(provider, instance.InstanceId);
        migrated.DefinitionId.Should().Be(second.Id);
        migrated.Migrations.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { SourceDefinitionId = first.Id, TargetDefinitionId = second.Id, MigratedByUserId = assignee });

        var keptTask = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        keptTask.Id.Should().Be(review.Id);
        keptTask.DefinitionId.Should().Be(second.Id);
        keptTask.CurrenAssignedUser.Should().Be(assignee);
        keptTask.Assignee.Should().Be("bert");

        // Die Zielversion fuehrt hinter "Review" zu einer weiteren Aufgabe. Erscheint sie,
        // folgt die Instanz nachweislich dem neuen Modell und nicht dem mitgefuehrten alten.
        await CompleteAsync(new BpmnBusinessLogic(provider), keptTask);
        var afterCompletion = await TasksAsync(provider, instance.InstanceId);
        afterCompletion.Should().ContainSingle().Which.Token.CurrentFlowNode!.Id.Should().Be("Approve");

        // Der Abschluss schreibt die Instanz neu; ohne Uebernahme waere die Spur nun leer.
        (await InstanceAsync(provider, instance.InstanceId)).Migrations.Should().ContainSingle();
    }

    /// <summary>
    /// Ein privater Entwurf ueberlebt nur bei unveraenderter Formularbindung. Der Trockenlauf
    /// sagt beides vorher, damit niemand seine Eingaben unangekuendigt verliert.
    /// </summary>
    internal static async Task DraftAsync(ITransactionalStorageProvider provider, bool changeForm)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        var review = (await TasksAsync(provider, instance.InstanceId)).Single();
        var ownerKey = new string('a', 64);
        using (var storage = provider.GetTransactionalStorage())
        {
            (await storage.UserTaskDraftStorage.TrySave(new UserTaskDraft
            {
                UserTaskId = review.Id,
                OwnerKey = ownerKey,
                OwnerUserId = Guid.NewGuid(),
                TokenId = review.Token.Id,
                ProcessInstanceId = instance.InstanceId,
                DefinitionId = review.DefinitionId,
                Revision = 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                DataJson = """{"answer":"halb fertig"}"""
            }, expectedRevision: 0)).Status.Should().Be(UserTaskDraftWriteStatus.Written);
            storage.CommitChanges();
        }

        var target = await DeployAsync(provider, engine, new Model.Version(2, 0),
            Xml(changeForm ? SecondForm : FirstForm, withApprove: true), additionalForms: [SecondForm]);

        var notices = (await engine.PreviewInstanceMigration([instance.InstanceId]))
            .Instances.Single().Notices;
        notices.Select(notice => notice.Code).Should().BeEquivalentTo(changeForm
            ? new[] { InstanceMigrationCodes.UserTaskFormChanged, InstanceMigrationCodes.UserTaskDraftDiscarded }
            : []);
        notices.Should().OnlyContain(notice => notice.FlowNodeId == "Review");

        (await engine.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid()))
            .Instances.Single().Migrated.Should().BeTrue();

        using var reader = provider.GetTransactionalStorage();
        var draft = await reader.UserTaskDraftStorage.Get(review.Id, ownerKey);
        if (changeForm)
        {
            draft.Should().BeNull();
            return;
        }

        draft.Should().NotBeNull();
        draft!.DefinitionId.Should().Be(target.Id, "the draft must match the task's new binding");
        draft.Revision.Should().Be(1);
        draft.DataJson.Should().Be("""{"answer":"halb fertig"}""");
    }

    /// <summary>
    /// Eine nicht deckungsgleiche Instanz bleibt unveraendert liegen, ohne die migrierbare
    /// Instanz derselben Anfrage aufzuhalten.
    /// </summary>
    internal static async Task PartialAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        var source = await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var migratable = await StartAsync(engine, "left");
        var blocked = await StartAsync(engine, "right");
        var target = await DeployAsync(provider, engine, new Model.Version(2, 0),
            Xml(FirstForm, withApprove: true, secondNodeId: "SecondRenamed"));

        var preview = await engine.PreviewInstanceMigration([migratable.InstanceId, blocked.InstanceId]);
        preview.Instances.Single(item => item.InstanceId == blocked.InstanceId).Problems
            .Should().ContainSingle().Which.Should().BeEquivalentTo(
                new { Code = nameof(InstanceMigrationProblemCode.FlowNodeMissing), FlowNodeId = "Second" });

        var outcome = await engine.MigrateInstances(
            [migratable.InstanceId, blocked.InstanceId], target.Id, Guid.NewGuid());

        outcome.Instances.Single(item => item.InstanceId == migratable.InstanceId).Migrated.Should().BeTrue();
        var rejected = outcome.Instances.Single(item => item.InstanceId == blocked.InstanceId);
        rejected.Migrated.Should().BeFalse();
        rejected.Problems.Select(problem => problem.Code)
            .Should().Equal(nameof(InstanceMigrationProblemCode.FlowNodeMissing));

        (await InstanceAsync(provider, migratable.InstanceId)).DefinitionId.Should().Be(target.Id);
        var untouched = await InstanceAsync(provider, blocked.InstanceId);
        untouched.DefinitionId.Should().Be(source.Id);
        untouched.Migrations.Should().BeEmpty();
        (await TasksAsync(provider, blocked.InstanceId)).Should().ContainSingle()
            .Which.DefinitionId.Should().Be(source.Id);
    }

    /// <summary>
    /// Ein Worker, der gerade arbeitet, behaelt seinen Auftrag samt Sperre und meldet danach
    /// unveraendert zurueck; die Instanz laeuft dann auf dem Weg der Zielversion weiter.
    /// </summary>
    internal static async Task ServiceTaskAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), ServiceXml(withApprove: false));
        var instance = await engine.StartProcessInstance(MetaDefinitionId);
        ServiceTaskJob claimed;
        using (var storage = provider.GetTransactionalStorage())
        {
            claimed = (await storage.ServiceTaskStorage.ClaimJobs(
                "fetch", "worker-1", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5), 5))
                .Should().ContainSingle().Subject;
            storage.CommitChanges();
        }

        var target = await DeployAsync(provider, engine, new Model.Version(2, 0), ServiceXml(withApprove: true));

        var preview = await engine.PreviewInstanceMigration([instance.InstanceId]);
        preview.Instances.Single().Notices.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Code = InstanceMigrationCodes.ServiceTaskJobInProgress, FlowNodeId = "Fetch" });

        (await engine.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid()))
            .Instances.Single().Migrated.Should().BeTrue();

        ServiceTaskJob stillLocked;
        using (var storage = provider.GetTransactionalStorage())
        {
            var job = await storage.ServiceTaskStorage.GetJob(claimed.Id);
            job.Should().NotBeNull();
            job!.DefinitionId.Should().Be(target.Id);
            job.LockedBy.Should().Be("worker-1");
            job.LockedUntil.Should().BeCloseTo(claimed.LockedUntil!.Value, TimeSpan.FromSeconds(1));
            job.Retries.Should().Be(claimed.Retries);
            var locked = await storage.ServiceTaskStorage.GetLockedJob(claimed.Id, "worker-1", DateTime.UtcNow);
            locked.Should().NotBeNull();
            stillLocked = locked!;
        }

        await new BpmnBusinessLogic(provider).CompleteServiceTaskJob(
            stillLocked, new ExpandoObject(), Guid.NewGuid());

        (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.Token.CurrentFlowNode!.Id.Should().Be("Approve");
    }

    /// <summary>
    /// Nachrichten- und Timeranmeldungen werden aus dem Tokenstand neu geschrieben und tragen
    /// danach die Zielversion; sonst suchte ein eintreffendes Ereignis die falsche Version.
    /// </summary>
    internal static async Task SubscriptionsAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        var source = await DeployAsync(provider, engine, new Model.Version(1, 0), WaitingXml(withApprove: false));
        var instance = await engine.StartProcessInstance(MetaDefinitionId);
        using (var storage = provider.GetTransactionalStorage())
        {
            (await storage.SubscriptionStorage.GetTimerSubscriptions(instance.InstanceId))
                .Should().ContainSingle().Which.DefinitionId.Should().Be(source.Id);
            (await storage.SubscriptionStorage.GetMessageSubscription(instance.InstanceId))
                .Should().ContainSingle().Which.DefinitionId.Should().Be(source.Id);
        }

        var target = await DeployAsync(provider, engine, new Model.Version(2, 0), WaitingXml(withApprove: true));
        (await engine.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid()))
            .Instances.Single().Migrated.Should().BeTrue();

        using var reader = provider.GetTransactionalStorage();
        (await reader.SubscriptionStorage.GetTimerSubscriptions(instance.InstanceId))
            .Should().ContainSingle().Which.DefinitionId.Should().Be(target.Id);
        (await reader.SubscriptionStorage.GetMessageSubscription(instance.InstanceId))
            .Should().ContainSingle().Which.DefinitionId.Should().Be(target.Id);
    }

    /// <summary>
    /// Ein zweiter API-Prozess deployt mitten im Stapel. Die Engine-Sperre gilt nur im eigenen
    /// Prozess; erkennt der Umzug die neue Version nicht, haengte er die restlichen Instanzen an
    /// eine Version, die der Aufrufer nie bestaetigt hat.
    /// </summary>
    internal static async Task TargetVersionChangedAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        var source = await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var first = await StartAsync(engine, "left");
        var second = await StartAsync(engine, "left");
        var target = await DeployAsync(provider, engine, new Model.Version(2, 0), Xml(FirstForm, withApprove: true));

        // Der Griff greift vor dem Nachlesen der zweiten Instanz: erste Instanz gelesen (1),
        // erste Instanz migriert (2), zweite Instanz (3).
        var hooks = new MigrationStorageHooks
        {
            OnDeployedDefinitionRead = 3,
            BeforeDeployedDefinitionRead = async () => await DeployAsync(
                provider,
                // Ein eigener Geschaeftslogik-Aufbau steht fuer den zweiten API-Prozess: Er hat
                // seine eigene Engine-Sperre und kann deshalb waehrend des Stapels deployen.
                new BpmnBusinessLogic(provider),
                new Model.Version(3, 0),
                Xml(FirstForm, withApprove: true, secondNodeId: "Third"))
        };

        var outcome = await new BpmnBusinessLogic(new HookedTransactionalStorageProvider(provider, hooks))
            .MigrateInstances([first.InstanceId, second.InstanceId], target.Id, Guid.NewGuid());

        outcome.Status.Should().Be(InstanceMigrationRequestStatus.Accepted);
        outcome.Instances.Single(item => item.InstanceId == first.InstanceId).Migrated.Should().BeTrue();
        var stopped = outcome.Instances.Single(item => item.InstanceId == second.InstanceId);
        stopped.Migrated.Should().BeFalse();
        stopped.Problems.Select(problem => problem.Code)
            .Should().Equal(InstanceMigrationCodes.TargetVersionChanged);

        // Die frueheren Instanzen des Stapels bleiben migriert; die spaetere bleibt unberuehrt.
        (await InstanceAsync(provider, first.InstanceId)).DefinitionId.Should().Be(target.Id);
        var untouched = await InstanceAsync(provider, second.InstanceId);
        untouched.DefinitionId.Should().Be(source.Id);
        untouched.Migrations.Should().BeEmpty();
    }

    /// <summary>
    /// Eine Ablage, die Entwuerfe fuehrt, den Entwurfsvertrag aber nicht kennt, darf nicht
    /// halb umziehen: Die Vorschau sagt es vorher, und der Umzug laesst die Instanz liegen.
    /// </summary>
    internal static async Task DraftStorageNotSupportedAsync(ITransactionalStorageProvider provider)
    {
        var legacy = new LegacyDraftStorageProvider(provider);
        var engine = new BpmnBusinessLogic(provider);
        var source = await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        var target = await DeployAsync(provider, engine, new Model.Version(2, 0), Xml(FirstForm, withApprove: true));

        var legacyEngine = new BpmnBusinessLogic(legacy);
        var preview = await legacyEngine.PreviewInstanceMigration([instance.InstanceId]);
        var item = preview.Instances.Should().ContainSingle().Subject;
        item.Migratable.Should().BeFalse();
        item.Problems.Select(problem => problem.Code)
            .Should().Contain(InstanceMigrationCodes.DraftStorageNotSupported);

        var outcome = await legacyEngine.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid());
        var result = outcome.Instances.Should().ContainSingle().Subject;
        result.Migrated.Should().BeFalse();
        result.Problems.Select(problem => problem.Code)
            .Should().Contain(InstanceMigrationCodes.DraftStorageNotSupported);

        var untouched = await InstanceAsync(provider, instance.InstanceId);
        untouched.DefinitionId.Should().Be(source.Id);
        untouched.Migrations.Should().BeEmpty();
        (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.DefinitionId.Should().Be(source.Id);
    }

    /// <summary>
    /// Eine Ablage, die ausdruecklich keine Entwuerfe fuehrt, haelt den Umzug nicht auf: Sie
    /// kann keinen Entwurf gespeichert haben und antwortet deshalb wahrheitsgemaess mit "keine".
    /// </summary>
    internal static async Task NoDraftStorageAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        var target = await DeployAsync(provider, engine, new Model.Version(2, 0), Xml(SecondForm, withApprove: true),
            additionalForms: [SecondForm]);

        var withoutDrafts = new BpmnBusinessLogic(new NoDraftStorageProvider(provider));
        var preview = await withoutDrafts.PreviewInstanceMigration([instance.InstanceId]);
        var item = preview.Instances.Should().ContainSingle().Subject;
        item.Migratable.Should().BeTrue();
        // Das geaenderte Formular bleibt ein Hinweis; ein verworfener Entwurf kann es nicht geben.
        item.Notices.Select(notice => notice.Code).Should().Equal(InstanceMigrationCodes.UserTaskFormChanged);

        (await withoutDrafts.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid()))
            .Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();
        (await InstanceAsync(provider, instance.InstanceId)).DefinitionId.Should().Be(target.Id);
    }

    /// <summary>
    /// Der Trockenlauf warnt vor Timern am wartenden Knoten: Nach dem Umzug rechnen sie mit der
    /// Dauer der Zielversion ab dem urspruenglichen Beginn des Wartens — auch ein in der
    /// Zielversion neu angehefteter Boundary-Timer.
    /// </summary>
    internal static async Task TimerNoticeAsync(ITransactionalStorageProvider provider, bool boundaryTimer)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), TimerXml(withBoundaryTimer: false));
        var instance = await engine.StartProcessInstance(MetaDefinitionId);
        await DeployAsync(provider, engine, new Model.Version(2, 0), TimerXml(withBoundaryTimer: boundaryTimer));

        var notices = (await engine.PreviewInstanceMigration([instance.InstanceId])).Instances.Single().Notices;

        // Ohne Boundary-Timer wartet die Instanz am Timer-Catch-Event selbst, mit Boundary-Timer
        // zusaetzlich an der Aufgabe, an der er in der Zielversion neu haengt.
        notices.Where(notice => notice.Code == InstanceMigrationCodes.TimerRecalculated)
            .Select(notice => notice.FlowNodeId)
            .Should().BeEquivalentTo(boundaryTimer ? new[] { "Wait", "Review" } : ["Wait"]);
    }

    /// <summary>Eine Instanz, die bereits auf der deployten Version laeuft, ist kein Umzug.</summary>
    internal static async Task AlreadyOnTargetAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        var deployed = await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");

        var preview = await engine.PreviewInstanceMigration([instance.InstanceId]);
        var item = preview.Instances.Should().ContainSingle().Subject;
        item.Migratable.Should().BeFalse();
        item.Problems.Should().ContainSingle().Which.Code
            .Should().Be(InstanceMigrationCodes.AlreadyOnTargetVersion);

        var outcome = await engine.MigrateInstances([instance.InstanceId], deployed.Id, Guid.NewGuid());
        outcome.Instances.Should().ContainSingle().Which.Migrated.Should().BeFalse();
        (await InstanceAsync(provider, instance.InstanceId)).Migrations.Should().BeEmpty();
    }

    /// <summary>
    /// Der Kern der Zuordnung von Hand: Der wartende Knoten fehlt in der Zielversion, der
    /// Trockenlauf fragt danach, und mit der Antwort zieht die Instanz um. Der Umzug fasst den
    /// Plan in seiner eigenen Transaktion neu — ohne die Zuordnung dort bliebe die Instanz
    /// liegen, obwohl der Trockenlauf sie als migrierbar ausgewiesen hat.
    /// </summary>
    internal static async Task MappingAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        var review = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        var assignee = Guid.NewGuid();
        using (var storage = provider.GetTransactionalStorage())
        {
            review.CurrenAssignedUser = assignee;
            review.Assignee = "bert";
            await storage.SubscriptionStorage.AddUserTaskSubscription(review);
            storage.CommitChanges();
        }

        var target = await DeployAsync(provider, engine, new Model.Version(2, 0),
            Xml(FirstForm, withApprove: true, reviewNodeId: RenamedReviewNodeId));

        // Ohne Zuordnung bleibt die Instanz liegen; der Trockenlauf nennt den offenen Knoten
        // und die Knoten der Zielversion, unter denen die Bedienung waehlen kann.
        var open = await engine.PreviewInstanceMigration([instance.InstanceId]);
        var blocked = open.Instances.Should().ContainSingle().Subject;
        blocked.Migratable.Should().BeFalse();
        blocked.Problems.Select(problem => problem.Code)
            .Should().Equal(nameof(InstanceMigrationProblemCode.FlowNodeMissing));
        open.MappingRequired.Should().ContainSingle().Which
            .Should().Be(new InstanceMigrationFlowNode("Review", "Review", "UserTask"));
        open.MappingTargets.Should().Contain(node => node.Id == RenamedReviewNodeId && node.Type == "UserTask");
        open.MappingTargets.Should().Contain(node => node.Id == "Split" && node.Type == "ExclusiveGateway");

        var mapping = new Dictionary<string, string> { ["Review"] = RenamedReviewNodeId };
        var mapped = await engine.PreviewInstanceMigration([instance.InstanceId], mapping);
        mapped.Instances.Should().ContainSingle().Which.Migratable.Should().BeTrue();
        mapped.MappingRequired.Should().BeEmpty();

        (await engine.MigrateInstances([instance.InstanceId], target.Id, assignee, mapping))
            .Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();

        (await InstanceAsync(provider, instance.InstanceId)).DefinitionId.Should().Be(target.Id);
        var keptTask = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        keptTask.Id.Should().Be(review.Id);
        keptTask.DefinitionId.Should().Be(target.Id);
        keptTask.CurrenAssignedUser.Should().Be(assignee);
        keptTask.Assignee.Should().Be("bert");
        keptTask.Token.CurrentFlowNode!.Id.Should().Be(RenamedReviewNodeId);

        // Hinter dem zugeordneten Knoten gilt der Weg der Zielversion: "Approve" gibt es nur dort.
        await CompleteAsync(new BpmnBusinessLogic(provider), keptTask);
        (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.Token.CurrentFlowNode!.Id.Should().Be("Approve");
    }

    /// <summary>
    /// Eine Zuordnung auf einen Knoten, den die Zielversion nicht kennt, ist keine Antwort: Die
    /// Instanz bleibt unveraendert, und der Trockenlauf fragt denselben Knoten erneut ab.
    /// </summary>
    internal static async Task MappingTargetMissingAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        var source = await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        var target = await DeployAsync(provider, engine, new Model.Version(2, 0),
            Xml(FirstForm, withApprove: true, reviewNodeId: RenamedReviewNodeId));

        var mapping = new Dictionary<string, string> { ["Review"] = "NichtVorhanden" };
        var preview = await engine.PreviewInstanceMigration([instance.InstanceId], mapping);
        var item = preview.Instances.Should().ContainSingle().Subject;
        item.Migratable.Should().BeFalse();
        item.Problems.Select(problem => problem.Code)
            .Should().Equal(nameof(InstanceMigrationProblemCode.MappingTargetMissing));
        preview.MappingRequired.Select(node => node.Id).Should().Equal("Review");

        var outcome = await engine.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid(), mapping);
        var result = outcome.Instances.Should().ContainSingle().Subject;
        result.Migrated.Should().BeFalse();
        result.Problems.Select(problem => problem.Code)
            .Should().Equal(nameof(InstanceMigrationProblemCode.MappingTargetMissing));

        var untouched = await InstanceAsync(provider, instance.InstanceId);
        untouched.DefinitionId.Should().Be(source.Id);
        untouched.Migrations.Should().BeEmpty();
        (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.DefinitionId.Should().Be(source.Id);
    }

    /// <summary>
    /// Liegt das Modell der Quellversion nicht mehr vor, bleibt die Frage trotzdem stellbar:
    /// Kennung und Typ kommen dann aus dem Element des wartenden Tokens, ein Name wird nicht
    /// geraten.
    /// </summary>
    internal static async Task MappingWithoutSourceModelAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        var source = await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        await DeployAsync(provider, engine, new Model.Version(2, 0),
            Xml(FirstForm, withApprove: true, reviewNodeId: RenamedReviewNodeId));

        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.DeleteBinary(source.Id);
            storage.CommitChanges();
        }

        var preview = await engine.PreviewInstanceMigration([instance.InstanceId]);

        preview.MappingRequired.Should().ContainSingle().Which
            .Should().Be(new InstanceMigrationFlowNode("Review", null, "UserTask"));
        preview.MappingTargets.Should().Contain(node => node.Id == RenamedReviewNodeId);
    }

    /// <summary>
    /// Eine Zuordnung ueber Elementtypen hinweg ergaebe einen Zustand, den das Zielmodell nicht
    /// kennt. Sie ist beantwortet und wird deshalb nicht erneut abgefragt, sondern abgelehnt.
    /// </summary>
    internal static async Task MappingTypeChangedAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        await DeployAsync(provider, engine, new Model.Version(2, 0),
            Xml(FirstForm, withApprove: true, reviewNodeId: RenamedReviewNodeId));

        var preview = await engine.PreviewInstanceMigration(
            [instance.InstanceId], new Dictionary<string, string> { ["Review"] = "Split" });

        var item = preview.Instances.Should().ContainSingle().Subject;
        item.Migratable.Should().BeFalse();
        item.Problems.Select(problem => problem.Code)
            .Should().Equal(nameof(InstanceMigrationProblemCode.FlowNodeTypeChanged));
        preview.MappingRequired.Should().BeEmpty();
    }

    /// <summary>
    /// Die Zuordnung gilt fuer die ganze Anfrage: Eine Instanz, die am zugeordneten Knoten
    /// wartet, zieht mit ihm um; eine Instanz an einem unveraenderten Knoten zieht um wie bisher.
    /// </summary>
    internal static async Task MappingPartialAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var mappedInstance = await StartAsync(engine, "left");
        var untouchedNode = await StartAsync(engine, "right");
        var target = await DeployAsync(provider, engine, new Model.Version(2, 0),
            Xml(FirstForm, withApprove: true, reviewNodeId: RenamedReviewNodeId));

        var mapping = new Dictionary<string, string> { ["Review"] = RenamedReviewNodeId };
        var preview = await engine.PreviewInstanceMigration(
            [mappedInstance.InstanceId, untouchedNode.InstanceId], mapping);
        preview.Instances.Should().OnlyContain(item => item.Migratable);
        preview.MappingRequired.Should().BeEmpty();

        var outcome = await engine.MigrateInstances(
            [mappedInstance.InstanceId, untouchedNode.InstanceId], target.Id, Guid.NewGuid(), mapping);
        outcome.Instances.Should().OnlyContain(item => item.Migrated);

        (await TasksAsync(provider, mappedInstance.InstanceId)).Should().ContainSingle()
            .Which.Token.CurrentFlowNode!.Id.Should().Be(RenamedReviewNodeId);
        var unchangedNode = (await TasksAsync(provider, untouchedNode.InstanceId)).Should().ContainSingle().Subject;
        unchangedNode.Token.CurrentFlowNode!.Id.Should().Be("Second");
        unchangedNode.DefinitionId.Should().Be(target.Id);
    }

    /// <summary>
    /// Die Zuordnung fuehrt die Aufgabe auf einen Knoten, der ein anderes Formular bindet. Der
    /// Umzug entscheidet ueber den privaten Entwurf am zugeordneten Knoten; der Trockenlauf muss
    /// deshalb dort vergleichen und nicht am gleichnamigen Knoten der Zielversion — sonst
    /// verschwaende ein Entwurf unangekuendigt.
    /// </summary>
    internal static async Task MappingFormChangedAsync(ITransactionalStorageProvider provider, bool withDraft)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), Xml(FirstForm, withApprove: false));
        var instance = await StartAsync(engine, "left");
        var review = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        var ownerKey = new string('b', 64);
        if (withDraft)
        {
            using var writer = provider.GetTransactionalStorage();
            (await writer.UserTaskDraftStorage.TrySave(new UserTaskDraft
            {
                UserTaskId = review.Id,
                OwnerKey = ownerKey,
                OwnerUserId = Guid.NewGuid(),
                TokenId = review.Token.Id,
                ProcessInstanceId = instance.InstanceId,
                DefinitionId = review.DefinitionId,
                Revision = 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                DataJson = """{"answer":"halb fertig"}"""
            }, expectedRevision: 0)).Status.Should().Be(UserTaskDraftWriteStatus.Written);
            writer.CommitChanges();
        }

        var target = await DeployAsync(provider, engine, new Model.Version(2, 0),
            Xml(SecondForm, withApprove: true, reviewNodeId: RenamedReviewNodeId), additionalForms: [SecondForm]);

        var mapping = new Dictionary<string, string> { ["Review"] = RenamedReviewNodeId };
        var item = (await engine.PreviewInstanceMigration([instance.InstanceId], mapping))
            .Instances.Should().ContainSingle().Subject;
        item.Migratable.Should().BeTrue();
        item.Notices.Select(notice => notice.Code).Should().BeEquivalentTo(withDraft
            ? new[] { InstanceMigrationCodes.UserTaskFormChanged, InstanceMigrationCodes.UserTaskDraftDiscarded }
            : [InstanceMigrationCodes.UserTaskFormChanged]);

        // Der Hinweis nennt den Knoten, den die Bedienung sieht und zuordnet: den der Quellversion.
        item.Notices.Should().OnlyContain(notice => notice.FlowNodeId == "Review");

        (await engine.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid(), mapping))
            .Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();

        using var reader = provider.GetTransactionalStorage();
        var keptTask = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        keptTask.Id.Should().Be(review.Id);
        keptTask.Token.CurrentFlowNode!.Id.Should().Be(RenamedReviewNodeId);
        // Genau das, was der Trockenlauf angekuendigt hat: Der Entwurf ist fort.
        (await reader.UserTaskDraftStorage.Get(review.Id, ownerKey)).Should().BeNull();
    }

    /// <summary>
    /// Die Zuordnung fuehrt die Aufgabe auf einen Knoten, an dem in der Zielversion ein
    /// Boundary-Timer haengt. Er steht nach dem Umzug sofort scharf; der Trockenlauf muss ihn am
    /// Quellknoten ankuendigen, statt ihn zu uebersehen.
    /// </summary>
    internal static async Task MappingTimerAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), TimerXml(withBoundaryTimer: false));
        var instance = await engine.StartProcessInstance(MetaDefinitionId);
        var target = await DeployAsync(provider, engine, new Model.Version(2, 0),
            TimerXml(withBoundaryTimer: true, reviewNodeId: RenamedReviewNodeId));

        var mapping = new Dictionary<string, string> { ["Review"] = RenamedReviewNodeId };
        var item = (await engine.PreviewInstanceMigration([instance.InstanceId], mapping))
            .Instances.Should().ContainSingle().Subject;
        item.Migratable.Should().BeTrue();

        // "Wait" traegt den Timer in beiden Versionen, "Review" erst durch die Zuordnung.
        item.Notices.Where(notice => notice.Code == InstanceMigrationCodes.TimerRecalculated)
            .Select(notice => notice.FlowNodeId)
            .Should().BeEquivalentTo(["Wait", "Review"]);
        item.Notices.Should().NotContain(notice => notice.FlowNodeId == RenamedReviewNodeId);

        (await engine.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid(), mapping))
            .Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();
        (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.Token.CurrentFlowNode!.Id.Should().Be(RenamedReviewNodeId);
    }

    /// <summary>
    /// Eine Zuordnung verschiebt auch den Auftrag eines wartenden Service-Tasks. Danach muss der
    /// Auftrag den Knoten tragen, auf dem das Token wirklich steht — Sperre, Versuche und
    /// Kennung bleiben davon unberuehrt, und der arbeitende Worker meldet unveraendert zurueck.
    /// </summary>
    internal static async Task MappingServiceTaskAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, new Model.Version(1, 0), ServiceXml(withApprove: false));
        var instance = await engine.StartProcessInstance(MetaDefinitionId);
        ServiceTaskJob claimed;
        using (var storage = provider.GetTransactionalStorage())
        {
            claimed = (await storage.ServiceTaskStorage.ClaimJobs(
                "fetch", "worker-1", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5), 5))
                .Should().ContainSingle().Subject;
            storage.CommitChanges();
        }

        claimed.FlowNodeId.Should().Be("Fetch");

        // Gleicher Auftragstyp, andere Kennung und anderer Name: genau der Fall, den die
        // Zuordnung beantwortet.
        var target = await DeployAsync(provider, engine, new Model.Version(2, 0),
            ServiceXml(withApprove: true, fetchNodeId: RenamedFetchNodeId, fetchName: RenamedFetchNodeName));

        var mapping = new Dictionary<string, string> { ["Fetch"] = RenamedFetchNodeId };
        (await engine.MigrateInstances([instance.InstanceId], target.Id, Guid.NewGuid(), mapping))
            .Instances.Should().ContainSingle().Which.Migrated.Should().BeTrue();

        ServiceTaskJob stillLocked;
        using (var storage = provider.GetTransactionalStorage())
        {
            (await storage.ServiceTaskStorage.GetJobs())
                .Where(job => job.ProcessInstanceId == instance.InstanceId)
                .Should().ContainSingle();
            var job = await storage.ServiceTaskStorage.GetJob(claimed.Id);
            job.Should().NotBeNull();
            job!.FlowNodeId.Should().Be(RenamedFetchNodeId);
            job.Name.Should().Be(RenamedFetchNodeName);
            job.DefinitionId.Should().Be(target.Id);
            job.Type.Should().Be(claimed.Type);
            job.LockedBy.Should().Be("worker-1");
            job.LockedUntil.Should().BeCloseTo(claimed.LockedUntil!.Value, TimeSpan.FromSeconds(1));
            job.Retries.Should().Be(claimed.Retries);
            var locked = await storage.ServiceTaskStorage.GetLockedJob(claimed.Id, "worker-1", DateTime.UtcNow);
            locked.Should().NotBeNull();
            stillLocked = locked!;
        }

        await new BpmnBusinessLogic(provider).CompleteServiceTaskJob(
            stillLocked, new ExpandoObject(), Guid.NewGuid());

        (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.Token.CurrentFlowNode!.Id.Should().Be("Approve");
    }

    internal static async Task<ProcessInstanceInfo> InstanceAsync(
        ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return await storage.InstanceStorage.GetProcessInstance(instanceId);
    }

    internal static async Task<UserTaskSubscription[]> TasksAsync(
        ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return (await storage.SubscriptionStorage.GetAllUserTasks(instanceId)).ToArray();
    }

    internal static async Task<ProcessInstanceInfo> StartAsync(
        BpmnBusinessLogic engine, string path, string? metaDefinitionId = null)
    {
        dynamic variables = new ExpandoObject();
        variables.path = path;
        return await engine.StartProcessInstance(
            metaDefinitionId ?? MetaDefinitionId, (ExpandoObject)variables);
    }

    internal static async Task<BpmnDefinition> DeployAsync(
        ITransactionalStorageProvider provider,
        BpmnBusinessLogic engine,
        Model.Version version,
        string xml,
        string? metaDefinitionId = null,
        string[]? additionalForms = null)
    {
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(), DefinitionId = metaDefinitionId ?? MetaDefinitionId, Version = version, Hash = "test",
            SavedByUser = Guid.NewGuid(), SavedOn = DateTime.UtcNow, IsActive = false
        };
        using (var storage = provider.GetTransactionalStorage())
        {
            // Der Katalogeintrag darf nur einmal entstehen; PostgreSQL meldet sonst einen Konflikt.
            if ((await storage.DefinitionStorage.GetAllMetaDefinitions())
                .All(meta => meta.DefinitionId != definition.DefinitionId))
                await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                {
                    DefinitionId = definition.DefinitionId, Name = "Migration"
                });
            await FormTestSeed.StoreAsync(storage, FirstForm, """{"components":[{"type":"textfield","key":"answer"}]}""");
            foreach (var form in additionalForms ?? [])
                await FormTestSeed.StoreAsync(storage, form, """{"components":[{"type":"textfield","key":"other"}]}""");
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
            storage.CommitChanges();
        }

        await engine.DeployDefinition(definition);
        return definition;
    }

    private static async Task CompleteAsync(BpmnBusinessLogic engine, UserTaskSubscription task)
    {
        (await engine.CompleteUserTaskAsync(new UserTaskResult
        {
            ProcessInstanceId = task.ProcessInstanceId,
            TokenId = task.Token.Id,
            FlowNodeId = task.Token.CurrentFlowNode!.Id,
            Data = new ExpandoObject()
        }, new CurrentUserContext(Guid.NewGuid(), "migration-test", false), canOperateAllTasks: true))
            .Should().Be(UserTaskCompletionOutcome.Completed);
    }

    /// <summary>
    /// Zwei Zweige hinter einem exklusiven Gateway: Damit koennen zwei Instanzen derselben
    /// Quellversion an verschiedenen Knoten warten — die Voraussetzung fuer den Teilerfolg.
    /// </summary>
    internal static string Xml(
        string reviewFormKey,
        bool withApprove,
        string secondNodeId = "Second",
        string reviewNodeId = "Review") => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="Definitions_Migration" targetNamespace="test">
          <bpmn:process id="Process_Migration" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:exclusiveGateway id="Split" default="Flow_Right" />
            <bpmn:userTask id="{{reviewNodeId}}" name="{{reviewNodeId}}"><bpmn:extensionElements>
              <zeebe:formDefinition formKey="{{reviewFormKey}}" />
            </bpmn:extensionElements></bpmn:userTask>
            {{(withApprove ? $$"""
              <bpmn:userTask id="Approve" name="Approve"><bpmn:extensionElements>
                <zeebe:formDefinition formKey="Approval" />
              </bpmn:extensionElements></bpmn:userTask>
              <bpmn:sequenceFlow id="Flow_Approve" sourceRef="{{reviewNodeId}}" targetRef="Approve" />
              <bpmn:sequenceFlow id="Flow_LeftEnd" sourceRef="Approve" targetRef="EndLeft" />
              """ : $$"""
              <bpmn:sequenceFlow id="Flow_LeftEnd" sourceRef="{{reviewNodeId}}" targetRef="EndLeft" />
              """)}}
            <bpmn:userTask id="{{secondNodeId}}" name="{{secondNodeId}}"><bpmn:extensionElements>
              <zeebe:formDefinition formKey="Approval" />
            </bpmn:extensionElements></bpmn:userTask>
            <bpmn:endEvent id="EndLeft" />
            <bpmn:endEvent id="EndRight" />
            <bpmn:sequenceFlow id="Flow_Start" sourceRef="Start" targetRef="Split" />
            <bpmn:sequenceFlow id="Flow_Left" sourceRef="Split" targetRef="{{reviewNodeId}}">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=path = "left"</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_Right" sourceRef="Split" targetRef="{{secondNodeId}}" />
            <bpmn:sequenceFlow id="Flow_RightEnd" sourceRef="{{secondNodeId}}" targetRef="EndRight" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static string ServiceXml(
        bool withApprove,
        string fetchNodeId = "Fetch",
        string fetchName = "Fetch") => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="Definitions_Migration" targetNamespace="test">
          <bpmn:process id="Process_Migration" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:serviceTask id="{{fetchNodeId}}" name="{{fetchName}}"><bpmn:extensionElements>
              <zeebe:taskDefinition type="fetch" retries="3" />
            </bpmn:extensionElements></bpmn:serviceTask>
            {{(withApprove ? $$"""
              <bpmn:userTask id="Approve" name="Approve"><bpmn:extensionElements>
                <zeebe:formDefinition formKey="Approval" />
              </bpmn:extensionElements></bpmn:userTask>
              <bpmn:sequenceFlow id="Flow_Approve" sourceRef="{{fetchNodeId}}" targetRef="Approve" />
              <bpmn:sequenceFlow id="Flow_End" sourceRef="Approve" targetRef="End" />
              """ : $$"""
              <bpmn:sequenceFlow id="Flow_End" sourceRef="{{fetchNodeId}}" targetRef="End" />
              """)}}
            <bpmn:endEvent id="End" />
            <bpmn:sequenceFlow id="Flow_Start" sourceRef="Start" targetRef="{{fetchNodeId}}" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Ein Timer-Catch-Event und eine Aufgabe nebeneinander. In der Zielversion kann an der
    /// Aufgabe zusaetzlich ein Boundary-Timer haengen, den die Quellversion nicht kannte.
    /// </summary>
    private static string TimerXml(bool withBoundaryTimer, string reviewNodeId = "Review") => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="Definitions_Migration" targetNamespace="test">
          <bpmn:process id="Process_Migration" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:parallelGateway id="Fork" />
            <bpmn:intermediateCatchEvent id="Wait">
              <bpmn:timerEventDefinition><bpmn:timeDuration>PT1H</bpmn:timeDuration></bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:userTask id="{{reviewNodeId}}" name="{{reviewNodeId}}"><bpmn:extensionElements>
              <zeebe:formDefinition formKey="Approval" />
            </bpmn:extensionElements></bpmn:userTask>
            {{(withBoundaryTimer ? $$"""
              <bpmn:boundaryEvent id="Escalate" attachedToRef="{{reviewNodeId}}">
                <bpmn:timerEventDefinition>
                  <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT1H</bpmn:timeDuration>
                </bpmn:timerEventDefinition>
              </bpmn:boundaryEvent>
              <bpmn:endEvent id="EndEscalate" />
              <bpmn:sequenceFlow id="Flow_Escalate" sourceRef="Escalate" targetRef="EndEscalate" />
              """ : "")}}
            <bpmn:endEvent id="EndTimer" />
            <bpmn:endEvent id="EndReview" />
            <bpmn:sequenceFlow id="Flow_Start" sourceRef="Start" targetRef="Fork" />
            <bpmn:sequenceFlow id="Flow_Timer" sourceRef="Fork" targetRef="Wait" />
            <bpmn:sequenceFlow id="Flow_Review" sourceRef="Fork" targetRef="{{reviewNodeId}}" />
            <bpmn:sequenceFlow id="Flow_EndTimer" sourceRef="Wait" targetRef="EndTimer" />
            <bpmn:sequenceFlow id="Flow_EndReview" sourceRef="{{reviewNodeId}}" targetRef="EndReview" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static string WaitingXml(bool withApprove) => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="Definitions_Migration" targetNamespace="test">
          <bpmn:process id="Process_Migration" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:parallelGateway id="Fork" />
            <bpmn:intermediateCatchEvent id="Wait">
              <bpmn:timerEventDefinition><bpmn:timeDuration>PT1H</bpmn:timeDuration></bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:intermediateCatchEvent id="Await">
              <bpmn:messageEventDefinition messageRef="Message_Continue" />
            </bpmn:intermediateCatchEvent>
            {{(withApprove ? """
              <bpmn:userTask id="Approve" name="Approve"><bpmn:extensionElements>
                <zeebe:formDefinition formKey="Approval" />
              </bpmn:extensionElements></bpmn:userTask>
              <bpmn:sequenceFlow id="Flow_Approve" sourceRef="Wait" targetRef="Approve" />
              <bpmn:sequenceFlow id="Flow_EndTimer" sourceRef="Approve" targetRef="EndTimer" />
              """ : """
              <bpmn:sequenceFlow id="Flow_EndTimer" sourceRef="Wait" targetRef="EndTimer" />
              """)}}
            <bpmn:endEvent id="EndTimer" />
            <bpmn:endEvent id="EndMessage" />
            <bpmn:sequenceFlow id="Flow_Start" sourceRef="Start" targetRef="Fork" />
            <bpmn:sequenceFlow id="Flow_Timer" sourceRef="Fork" targetRef="Wait" />
            <bpmn:sequenceFlow id="Flow_Message" sourceRef="Fork" targetRef="Await" />
            <bpmn:sequenceFlow id="Flow_EndMessage" sourceRef="Await" targetRef="EndMessage" />
          </bpmn:process>
          <bpmn:message id="Message_Continue" name="Continue" />
        </bpmn:definitions>
        """;
}
