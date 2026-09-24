using System.Dynamic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Model;
using Npgsql;
using PostgreSqlStorageSystem;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Jobs;
using WebApiEngine.Persistence;

namespace WebApiEngine.Tests;

/// <summary>
/// Belegt den Betriebsfall "Update mit laufenden Instanzen": eine Installation auf einem aelteren
/// Schemastand, gefuellt mit wartenden Vorgaengen, wird ueber denselben Weg wie `--migrate`
/// (<see cref="FlowzerStorageExtensions.RunMigrationsAsync"/>) auf den aktuellen Stand gehoben
/// und laeuft danach weiter. Dazu die beiden Eigenschaften, auf die sich ein Deployment
/// verlaesst: der Migrationsschritt ist wiederholbar und vertraegt einen zweiten Lauf daneben.
/// </summary>
public partial class PostgreSqlStorageIntegrationTest
{
    // Version 013 ist die erste Migration nach dem Stand, auf dem alle drei Wartezustaende
    // (Aufgabe, Auftrag, Timer) schon gespeichert werden koennen: Instanzen und Subscriptions
    // stammen aus 001, Auftraege aus 002, die Knotenhistorie aus 012. Das ist die aelteste
    // Ausgangslage, von der aus ein Update mit laufenden Instanzen geprueft wird.
    private const int OldestUpgradeBaselineExclusiveMaxVersion = 13;

    // Ausgangslage der letzten produktiven Aktualisierung: 001 bis 019 waren angewendet, das
    // Update brachte nur 020 (DMN-Entscheidungen) hinzu.
    private const int LastProductiveUpgradeBaselineExclusiveMaxVersion = 20;

    private const string UpgradeApprovalFormName = "UpgradeApproval";

    // Testzweck: Instanzen, die auf einem aelteren Schemastand mit wartender Aufgabe, wartendem
    // Auftrag und wartendem Timer gespeichert wurden, ueberstehen den produktiven Update-Schritt
    // `--migrate` (alle fehlenden Migrationen, danach das Formularbindungs-Upgrade) und lassen
    // sich danach unveraendert weiterfuehren. Baseline 13 (Stand bis 012) traegt zusaetzlich
    // Deployments aus der Zeit vor den Formularbindungen, die erst das Upgrade wieder
    // abschliessbar macht; Baseline 20 ist der Sprung der letzten produktiven Aktualisierung
    // (nur 020) mit bereits gebundenen Deployments, die das Upgrade nicht anfassen darf.
    [TestCase(OldestUpgradeBaselineExclusiveMaxVersion, true)]
    [TestCase(LastProductiveUpgradeBaselineExclusiveMaxVersion, false)]
    public async Task Upgrade_ShouldKeepRunningInstancesUsableAcrossAllMigrations(
        int baselineExclusiveMaxVersion,
        bool deployedWithoutFormBindings)
    {
        var schema = $"flowzer_upgrade_{Guid.NewGuid():N}";
        var worker = Guid.NewGuid();
        var expectedVersions = PostgreSqlMigrator.AvailableVersions
            .Where(version => version >= baselineExclusiveMaxVersion)
            .ToArray();
        // Ohne ausstehende Migration waere der Fall kein Update mehr und bewiese nichts.
        expectedVersions.Should().NotBeEmpty();

        try
        {
            await ApplyMigrationsBelow(schema, baselineExclusiveMaxVersion);
            (await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema)).Pending
                .Should().Equal(expectedVersions);

            // Beide Ausgangslagen speichern Seed-Definitionen und alle drei Wartezustaende mit dem
            // heutigen Speicherpfad gleich; er braucht keine Tabelle ab 013. Die Faelle
            // unterscheiden sich allein im Stand der Formularbindungen der Deployments.
            var oldProvider = new PostgreSqlTransactionalStorageProvider(_dataSource!, schema);
            var oldEngine = new BpmnBusinessLogic(oldProvider);
            await SeedUpgradeDefinitionsAsync(oldProvider);

            await oldEngine.DeployDefinition(await LatestDefinitionAsync(oldProvider, "Upgrade_Review"));
            await oldEngine.DeployDefinition(await LatestDefinitionAsync(oldProvider, "Upgrade_Service"));
            await oldEngine.DeployDefinition(await LatestDefinitionAsync(oldProvider, "Upgrade_Timer"));

            if (deployedWithoutFormBindings)
            {
                await RemoveFormBindingsAsync(oldProvider);
            }

            var waitingUserTask = await oldEngine.StartProcessInstance("Upgrade_Review");
            var waitingJob = await oldEngine.StartProcessInstance("Upgrade_Service");
            var waitingTimer = await oldEngine.StartProcessInstance("Upgrade_Timer");

            DateTime timerDueAt;
            using (var storage = oldProvider.GetTransactionalStorage())
            {
                (await storage.InstanceStorage.GetAllInstances()).Should().HaveCount(3);
                (await storage.SubscriptionStorage.GetAllUserTasks(waitingUserTask.InstanceId)).Should().ContainSingle();
                (await storage.ServiceTaskStorage.GetJobs()).Should().ContainSingle()
                    .Which.ProcessInstanceId.Should().Be(waitingJob.InstanceId);
                timerDueAt = (await storage.SubscriptionStorage.GetAllTimerSubscriptions())
                    .Single(subscription => subscription.ProcessInstanceId == waitingTimer.InstanceId).DueAt;
                (await storage.DefinitionStorage.GetAllDefinitions())
                    .Should().HaveCount(3)
                    .And.OnlyContain(definition => (definition.FormBindings == null) == deployedWithoutFormBindings);
            }

            // Das eigentliche Update, genau wie `--migrate` es ausfuehrt: alle noch fehlenden
            // Migrationen in einem Lauf, danach das Formularbindungs-Upgrade unter Tabellensperre.
            var logger = new CapturingLogger<PostgreSqlStorageIntegrationTest>();
            (await FlowzerStorageExtensions.RunMigrationsAsync(MigrationConfiguration(schema), logger))
                .Should().Be(0);

            // Genau die Versionen ab der Baseline wurden angewendet: Die Historie kennt danach
            // jede verfuegbare Version genau einmal, und die Meldung nennt nur die neuen.
            var status = await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema);
            status.IsUpToDate.Should().BeTrue();
            status.Applied.Should().OnlyHaveUniqueItems();
            status.Applied.Should().Equal(PostgreSqlMigrator.AvailableVersions);
            // Die Logzeilen sind ein Beleg fuer den Betreiber, keine Schnittstelle: Geprueft
            // werden nur die praegenden Teile (angewendete Versionen, Zahl der ergaenzten
            // Bindungen), nicht der vollstaendige Wortlaut oder die Reihenfolge des Streams.
            var messages = logger.Entries.Select(entry => entry.Message).ToList();
            messages.Should().ContainSingle(message =>
                message.Contains($"Applied {expectedVersions.Length} PostgreSQL migration(s)")
                && message.Contains(string.Join(", ", expectedVersions)));
            messages.Should().ContainSingle(message =>
                message.Contains("Formularbindungen")
                && message.EndsWith($": {(deployedWithoutFormBindings ? 3 : 0)}"));

            // Bewusst frische Objekte: was jetzt noch geht, kommt aus der Ablage und nicht aus
            // einem im Speicher gehaltenen Rest des alten Laufs.
            var newProvider = new PostgreSqlTransactionalStorageProvider(_dataSource!, schema);
            var newEngine = new BpmnBusinessLogic(newProvider);
            using (var storage = newProvider.GetTransactionalStorage())
            {
                var instances = (await storage.InstanceStorage.GetAllInstances()).ToList();
                instances.Should().HaveCount(3);
                instances.Should().OnlyContain(instance => instance.State == ProcessInstanceState.Waiting);
                (await storage.ServiceTaskStorage.GetJobs()).Should().ContainSingle();
                (await storage.SubscriptionStorage.GetAllTimerSubscriptions()).Should().ContainSingle();

                // Nach dem Update ist jede deployte Version gebunden, und zwar an die einzige
                // vorhandene Fassung des Formulars. Bei Baseline 20 ist das die Bindung aus dem
                // Deployment, bei Baseline 13 die des Upgrades.
                var approvalFormId = (await storage.FormStorage.GetFormMetadatas())
                    .Single(form => form.Name == UpgradeApprovalFormName).FormId;
                var approvalVersion = (await storage.FormStorage.GetForms(approvalFormId)).Should().ContainSingle().Which;
                var definitions = await storage.DefinitionStorage.GetAllDefinitions();
                definitions.Should().OnlyContain(definition => definition.FormBindings != null);
                var reviewBindings = definitions.Single(definition => definition.DefinitionId == "Upgrade_Review").FormBindings!;
                reviewBindings.Keys.Should().Equal(UpgradeApprovalFormName);
                reviewBindings[UpgradeApprovalFormName].Id.Should().Be(approvalVersion.Id);
                definitions.Where(definition => definition.DefinitionId != "Upgrade_Review")
                    .Should().OnlyContain(definition => definition.FormBindings!.Count == 0);
            }

            // 1. Die wartende Aufgabe laesst sich abschliessen; ohne Bindung schluege schon die
            //    Formularpruefung fehl (form.binding_missing).
            UserTaskSubscription subscription;
            using (var storage = newProvider.GetTransactionalStorage())
            {
                subscription = (await storage.SubscriptionStorage.GetAllUserTasks(waitingUserTask.InstanceId)).Single();
            }

            (await newEngine.CompleteUserTaskAsync(
                new UserTaskResult
                {
                    ProcessInstanceId = subscription.ProcessInstanceId,
                    TokenId = subscription.Token.Id,
                    FlowNodeId = subscription.Token.CurrentFlowNode!.Id,
                    Data = new ExpandoObject()
                },
                new CurrentUserContext(Guid.NewGuid(), "upgrade-test", false),
                canOperateAllTasks: true)).Should().Be(UserTaskCompletionOutcome.Completed);

            // 2. Der wartende Auftrag laesst sich holen und zurueckmelden.
            var jobService = new ServiceTaskJobService(
                newProvider,
                newEngine,
                new FakeTimeProvider(DateTimeOffset.UtcNow),
                NullLogger<ServiceTaskJobService>.Instance);
            var job = (await jobService.FetchAndLock("upgrade-zahlung", worker, "worker-a", 10, TimeSpan.FromMinutes(5)))
                .Should().ContainSingle().Which;
            job.ProcessInstanceId.Should().Be(waitingJob.InstanceId);
            (await jobService.Complete(job.Id, worker, "worker-a", null)).Should().Be(JobOperationResult.Ok);

            // 3. Der wartende Timer feuert.
            (await newEngine.HandleTime(timerDueAt.AddSeconds(1))).Should().Be(1);

            using (var storage = newProvider.GetTransactionalStorage())
            {
                var instances = (await storage.InstanceStorage.GetAllInstances()).ToList();
                instances.Should().HaveCount(3);
                instances.Should().OnlyContain(instance => instance.State == ProcessInstanceState.Completed);
                instances.Select(instance => instance.InstanceId).Should().BeEquivalentTo(
                    new[] { waitingUserTask.InstanceId, waitingJob.InstanceId, waitingTimer.InstanceId });
                (await storage.ServiceTaskStorage.GetJobs()).Should().BeEmpty();
                (await storage.SubscriptionStorage.GetAllTimerSubscriptions()).Should().BeEmpty();
                (await storage.SubscriptionStorage.GetAllUserTasks(waitingUserTask.InstanceId)).Should().BeEmpty();
            }
        }
        finally
        {
            await DropSchemaAsync(schema);
        }
    }

    // Testzweck: `--migrate` ist wiederholbar: Ein zweiter Lauf gegen dieselbe Ablage wendet
    // nichts erneut an, meldet Erfolg und laesst genau einen Historieneintrag je Migration zurueck.
    [Test]
    public async Task MigrateArgument_ShouldBeIdempotentWhenRunTwice()
    {
        var schema = $"flowzer_migrate_twice_{Guid.NewGuid():N}";
        var configuration = MigrationConfiguration(schema);

        try
        {
            (await FlowzerStorageExtensions.RunMigrationsAsync(configuration, NullLogger.Instance)).Should().Be(0);
            var afterFirst = await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema);
            afterFirst.IsUpToDate.Should().BeTrue();

            (await FlowzerStorageExtensions.RunMigrationsAsync(configuration, NullLogger.Instance)).Should().Be(0);
            var afterSecond = await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema);
            afterSecond.IsUpToDate.Should().BeTrue();
            afterSecond.Applied.Should().Equal(afterFirst.Applied);
            afterSecond.Applied.Should().OnlyHaveUniqueItems();
        }
        finally
        {
            await DropSchemaAsync(schema);
        }
    }

    // Testzweck: Zwei gleichzeitige Migrationslaeufe kommen sich nicht in die Quere: Der
    // Advisory-Lock des Migrators serialisiert sie, genau einer wendet jede Migration an,
    // und die Historie enthaelt anschliessend keinen doppelten Eintrag.
    [Test]
    public async Task ConcurrentMigrationRuns_ShouldBeSerializedByTheAdvisoryLock()
    {
        var schema = $"flowzer_migrate_race_{Guid.NewGuid():N}";

        try
        {
            var results = await Task.WhenAll(
                Task.Run(() => PostgreSqlMigrator.ApplyAsync(_connectionString, schema)),
                Task.Run(() => PostgreSqlMigrator.ApplyAsync(_connectionString, schema)));

            var expected = PostgreSqlMigrator.AvailableVersions;
            // Genau ein Lauf hat gearbeitet; der andere fand alles bereits vor.
            results.Select(applied => applied.Count).Order().Should().Equal(0, expected.Count);
            results.SelectMany(applied => applied).Should().BeEquivalentTo(expected);

            var status = await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema);
            status.IsUpToDate.Should().BeTrue();
            status.Applied.Should().OnlyHaveUniqueItems();
            status.Applied.Should().Equal(expected.Order());
        }
        finally
        {
            await DropSchemaAsync(schema);
        }
    }

    private IConfiguration MigrationConfiguration(string schema) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = FlowzerStorageOptions.ProviderPostgreSql,
            ["Storage:PostgreSql:ConnectionString"] = _connectionString,
            ["Storage:PostgreSql:Schema"] = schema
        })
        .Build();

    private async Task DropSchemaAsync(string schema)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {Quote(schema)} CASCADE", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task<BpmnDefinition> LatestDefinitionAsync(ITransactionalStorageProvider provider, string relatedDefinitionId)
    {
        using var storage = provider.GetTransactionalStorage();
        return await storage.DefinitionStorage.GetLatestDefinition(relatedDefinitionId);
    }

    private static async Task SeedUpgradeDefinitionsAsync(ITransactionalStorageProvider provider)
    {
        using var storage = provider.GetTransactionalStorage();
        await FormTestSeed.StoreAsync(storage, UpgradeApprovalFormName);
        await StoreAsync(storage, "Upgrade_Review", "Freigabe", UpgradeUserTaskXml);
        await StoreAsync(storage, "Upgrade_Service", "Zahlung", UpgradeServiceTaskXml);
        await StoreAsync(storage, "Upgrade_Timer", "Frist", UpgradeTimerXml);
        storage.CommitChanges();
    }

    // Stellt den Stand eines Deployments aus der Zeit vor den Formularbindungen her: Die Definition
    // traegt keine FormBindings. Das heutige Deployment bindet immer, deshalb wird die Bindung
    // nachtraeglich entfernt, bevor Instanzen gestartet werden.
    private static async Task RemoveFormBindingsAsync(ITransactionalStorageProvider provider)
    {
        using var storage = provider.GetTransactionalStorage();
        foreach (var definition in await storage.DefinitionStorage.GetAllDefinitions())
        {
            definition.FormBindings = null;
            await storage.DefinitionStorage.StoreDefinition(definition);
        }

        storage.CommitChanges();
    }

    private static async Task StoreAsync(ITransactionalStorage storage, string relatedDefinitionId, string name, string xml)
    {
        var definition = CreateDefinition(relatedDefinitionId, 1, 0, isActive: false);
        await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = relatedDefinitionId, Name = name });
        await storage.DefinitionStorage.StoreDefinition(definition);
        await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
    }

    private const string UpgradeUserTaskXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Upgrade_Review" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_UpgradeReview" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1"><bpmn:outgoing>Flow_1</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="UserTask_1" />
            <bpmn:userTask id="UserTask_1" name="Freigeben">
              <bpmn:extensionElements><zeebe:formDefinition formKey="UpgradeApproval" /></bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming><bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:userTask>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="UserTask_1" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1"><bpmn:incoming>Flow_2</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string UpgradeServiceTaskXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Upgrade_Service" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_UpgradeService" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1"><bpmn:outgoing>Flow_1</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:serviceTask id="ServiceTask_1" name="Zahlung ausloesen">
              <bpmn:extensionElements><zeebe:taskDefinition type="upgrade-zahlung" retries="3" /></bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming><bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1"><bpmn:incoming>Flow_2</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string UpgradeTimerXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Upgrade_Timer" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_UpgradeTimer" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1"><bpmn:outgoing>Flow_1</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="TimerCatch_1" />
            <bpmn:intermediateCatchEvent id="TimerCatch_1">
              <bpmn:incoming>Flow_1</bpmn:incoming><bpmn:outgoing>Flow_2</bpmn:outgoing>
              <bpmn:timerEventDefinition id="TimerDefinition_1">
                <bpmn:timeDuration>PT30M</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="Flow_2" sourceRef="TimerCatch_1" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1"><bpmn:incoming>Flow_2</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;
}
