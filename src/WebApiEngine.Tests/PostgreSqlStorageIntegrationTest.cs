using BPMN.Common;
using BPMN.HumanInteraction;
using BPMN.Process;
using FluentAssertions;
using Model;
using Npgsql;
using PostgreSqlStorageSystem;
using StorageSystem;
using StorageSystem.Exceptions;
using Testcontainers.PostgreSql;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>
/// Prueft die PostgreSQL-Ablage gegen einen echten PostgreSQL-Container (Testcontainers).
/// Ohne erreichbaren Docker-Daemon werden die Tests uebersprungen, nicht rot.
/// </summary>
[NonParallelizable]
public class PostgreSqlStorageIntegrationTest
{
    private const string Schema = "flowzer_test";
    private PostgreSqlContainer? _container;
    private NpgsqlDataSource? _dataSource;
    private string _connectionString = string.Empty;

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            Assert.Ignore($"PostgreSQL-Container nicht verfuegbar (Docker fehlt?): {exception.Message}");
            return;
        }

        _connectionString = _container.GetConnectionString();
        await PostgreSqlMigrator.ApplyAsync(_connectionString, Schema);
        _dataSource = new NpgsqlDataSourceBuilder(_connectionString).Build();
    }

    [OneTimeTearDown]
    public async Task StopDatabase()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [SetUp]
    public async Task ClearTables()
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = string.Join(";", new[]
        {
            "definitions", "definition_binaries", "meta_definitions", "instances",
            "message_subscriptions", "signal_subscriptions", "user_task_subscriptions", "timer_subscriptions", "forms", "form_metadata"
        }.Select(table => $"DELETE FROM {Schema}.{table}"));
        await command.ExecuteNonQueryAsync();
    }

    // Testzweck: Der Migrator ist idempotent: ein zweiter Lauf wendet nichts erneut an und laesst
    // genau einen Eintrag je Migration in der Historie zurueck.
    [Test]
    public async Task Migrator_ShouldBeIdempotent()
    {
        var appliedAgain = await PostgreSqlMigrator.ApplyAsync(_connectionString, Schema);

        appliedAgain.Should().BeEmpty();
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {Schema}.schema_migrations", connection);
        ((long)(await command.ExecuteScalarAsync())!).Should().Be(1);
    }

    // Testzweck: Definitionen, Binaerdaten und Katalog verhalten sich wie in der Dateiablage:
    // hoechste Version, deployte Version, NotFound und Conflict als fachliche Fehler.
    [Test]
    public async Task DefinitionStorage_ShouldMirrorFilesystemContract()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var v1 = CreateDefinition("catalog-1", 1, 0, isActive: false);
        var v2 = CreateDefinition("catalog-1", 2, 0, isActive: true);
        await storage.DefinitionStorage.StoreDefinition(v1);
        await storage.DefinitionStorage.StoreDefinition(v2);
        await storage.DefinitionStorage.StoreBinary(v2.Id, "<xml/>");
        await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = "catalog-1", Name = "Catalog" });

        (await storage.DefinitionStorage.GetMaxVersionId("catalog-1")).Should().Be(new Model.Version(2, 0));
        (await storage.DefinitionStorage.GetMaxVersionId("unknown")).Should().BeNull();
        (await storage.DefinitionStorage.GetLatestDefinition("catalog-1")).Id.Should().Be(v2.Id);
        (await storage.DefinitionStorage.GetDeployedDefinition("catalog-1"))!.Id.Should().Be(v2.Id);
        (await storage.DefinitionStorage.GetBinary(v2.Id)).Should().Be("<xml/>");
        (await storage.DefinitionStorage.GetAllBinaryDefinitions()).Should().Equal(v2.Id);
        var metas = await storage.DefinitionStorage.GetAllMetaDefinitions();
        metas.Should().ContainSingle().Which.DeployedId.Should().Be(v2.Id);
        metas[0].LatestVersion.Should().Be(new Model.Version(2, 0));

        await storage.DefinitionStorage.Invoking(s => s.GetDefinitionById(Guid.NewGuid())).Should().ThrowAsync<DefinitionStorageNotFoundException>();
        await storage.DefinitionStorage.Invoking(s => s.GetLatestDefinition("unknown")).Should().ThrowAsync<DefinitionStorageNotFoundException>();
        await storage.DefinitionStorage.Invoking(s => s.GetMetaDefinitionById("unknown")).Should().ThrowAsync<DefinitionStorageNotFoundException>();
        await storage.DefinitionStorage.Invoking(s => s.GetBinary(Guid.NewGuid())).Should().ThrowAsync<FileNotFoundException>();
        await storage.DefinitionStorage.Invoking(s => s.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = "catalog-1", Name = "Again" })).Should().ThrowAsync<DefinitionStorageConflictException>();
        await storage.DefinitionStorage.Invoking(s => s.UpdateMetaDefinition(new BpmnMetaDefinition { DefinitionId = "missing", Name = "x" })).Should().ThrowAsync<DefinitionStorageNotFoundException>();

        await storage.DefinitionStorage.UpdateMetaDefinition(new BpmnMetaDefinition { DefinitionId = "catalog-1", Name = "Renamed" });
        (await storage.DefinitionStorage.GetMetaDefinitionById("catalog-1")).Name.Should().Be("Renamed");
    }

    // Testzweck: Instanzen inklusive polymorpher Token-Elemente ueberleben den Weg durch die
    // Datenbank; unbekannte Ids sind FileNotFound (404 am API-Rand).
    [Test]
    public async Task InstanceStorage_ShouldRoundTripTokensAndFilterActiveInstances()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var active = CreateInstance(finished: false);
        var finished = CreateInstance(finished: true);
        await storage.InstanceStorage.AddOrUpdateInstance(active);
        await storage.InstanceStorage.AddOrUpdateInstance(finished);

        var loaded = await storage.InstanceStorage.GetProcessInstance(active.InstanceId);
        loaded.Tokens.Should().HaveCount(2);
        loaded.Tokens.Select(token => token.CurrentBaseElement).Should().ContainItemsAssignableTo<Process>();
        loaded.Tokens.Should().Contain(token => token.CurrentFlowNode is UserTask);
        (await storage.InstanceStorage.GetAllActiveInstances()).Select(i => i.InstanceId).Should().Equal(active.InstanceId);
        (await storage.InstanceStorage.GetAllInstances()).Should().HaveCount(2);
        await storage.InstanceStorage.Invoking(s => s.GetProcessInstance(Guid.NewGuid())).Should().ThrowAsync<FileNotFoundException>();
    }

    // Testzweck: Message-, Signal-, User-Task- und Timer-Subscriptions werden gezielt gefunden
    // und entfernt; mehrere Message-Subscriptions je Instanz bleiben erhalten.
    [Test]
    public async Task SubscriptionStorage_ShouldFindAndRemoveSubscriptionsByInstanceAndDefinition()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var instanceId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        await storage.SubscriptionStorage.AddMessageSubscription(new MessageSubscription(new MessageDefinition { Name = "A", FlowzerCorrelationKey = "k1" }, "P", "rel", definitionId, instanceId));
        await storage.SubscriptionStorage.AddMessageSubscription(new MessageSubscription(new MessageDefinition { Name = "B", FlowzerCorrelationKey = null }, "P", "rel", definitionId, instanceId));
        await storage.SubscriptionStorage.AddMessageSubscription(new MessageSubscription(new MessageDefinition { Name = "Start" }, "P", "rel", definitionId));
        storage.SubscriptionStorage.AddSignalSubscription(new SignalSubscription("S", "P", "rel", definitionId, instanceId));
        var timer = new TimerSubscription { DueAt = DateTime.UtcNow.AddMinutes(5), FlowNodeId = "T", Kind = TimerSubscriptionKind.IntermediateCatchEvent, ProcessId = "P", RelatedDefinitionId = "rel", DefinitionId = definitionId, ProcessInstanceId = instanceId };
        await storage.SubscriptionStorage.AddTimerSubscription(timer);
        await storage.SubscriptionStorage.AddTimerSubscription(new TimerSubscription { DueAt = DateTime.UtcNow.AddMinutes(1), FlowNodeId = "Start", Kind = TimerSubscriptionKind.ProcessStartEvent, ProcessId = "P", RelatedDefinitionId = "rel", DefinitionId = definitionId });

        (await storage.SubscriptionStorage.GetMessageSubscription(instanceId)).Should().HaveCount(2);
        (await storage.SubscriptionStorage.GetMessageSubscription("A", "k1", instanceId)).Should().ContainSingle();
        (await storage.SubscriptionStorage.GetMessageSubscription("B", null, instanceId)).Should().ContainSingle();
        (await storage.SubscriptionStorage.GetMessageSubscription("Start", null, null)).Should().ContainSingle();
        (await storage.SubscriptionStorage.GetSignalSubscriptions(instanceId)).Should().ContainSingle();
        (await storage.SubscriptionStorage.GetAllTimerSubscriptions()).Select(t => t.FlowNodeId).Should().Equal("Start", "T");

        await storage.SubscriptionStorage.AddTimerSubscription(new TimerSubscription { Id = timer.Id, DueAt = DateTime.UtcNow.AddMinutes(9), FlowNodeId = "T", Kind = timer.Kind, ProcessId = "P", RelatedDefinitionId = "rel", DefinitionId = definitionId, ProcessInstanceId = instanceId });
        (await storage.SubscriptionStorage.GetTimerSubscriptions(instanceId)).Should().ContainSingle().Which.DueAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(8));

        await storage.SubscriptionStorage.RemoveProcessMessageSubscriptionsByProcessInstanceId(instanceId);
        storage.SubscriptionStorage.RemoveProcessSingalSubscriptionsByProcessInstanceId(instanceId);
        await storage.SubscriptionStorage.RemoveProcessTimerSubscriptionsByProcessInstanceId(instanceId);
        (await storage.SubscriptionStorage.GetAllMessageSubscriptions()).Should().ContainSingle().Which.Message.Name.Should().Be("Start");
        (await storage.SubscriptionStorage.GetSignalSubscriptions(instanceId)).Should().BeEmpty();
        (await storage.SubscriptionStorage.GetAllTimerSubscriptions()).Should().ContainSingle();

        await storage.SubscriptionStorage.RemoveAllProcessMessageSubscriptionsWithNoInstancedId("rel");
        await storage.SubscriptionStorage.RemoveAllProcessTimerSubscriptionsWithNoInstanceId("rel");
        (await storage.SubscriptionStorage.GetAllMessageSubscriptions()).Should().BeEmpty();
        (await storage.SubscriptionStorage.GetAllTimerSubscriptions()).Should().BeEmpty();
    }

    // Testzweck: Die erweiterte User-Task-Liste traegt Katalogname und Definitionsversion;
    // fehlende Katalogeintraege fallen auf die technische Id zurueck statt zu scheitern.
    [Test]
    public async Task SubscriptionStorage_ShouldEnrichUserTasksWithCatalogNameAndVersion()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var definition = CreateDefinition("catalog-2", 3, 1, isActive: true);
        await storage.DefinitionStorage.StoreDefinition(definition);
        await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = "catalog-2", Name = "Reviews" });
        var instance = CreateInstance(finished: false);
        var token = instance.Tokens.Single(candidate => candidate.CurrentFlowNode is UserTask);
        await storage.SubscriptionStorage.AddUserTaskSubscription(new UserTaskSubscription
        {
            Id = Guid.NewGuid(), Name = "Review", Token = token, ProcessInstanceId = instance.InstanceId,
            MetaDefinitionId = "catalog-2", DefinitionId = definition.Id, ProcessId = "Process_1"
        });
        await storage.SubscriptionStorage.AddUserTaskSubscription(new UserTaskSubscription
        {
            Id = Guid.NewGuid(), Name = "Orphan", Token = token, ProcessInstanceId = Guid.NewGuid(),
            MetaDefinitionId = "missing-catalog", DefinitionId = Guid.NewGuid(), ProcessId = "Process_1"
        });

        var extended = (await storage.SubscriptionStorage.GetAllUserTasksExtended(Guid.NewGuid())).ToList();

        extended.Should().HaveCount(2);
        extended.Single(s => s.Name == "Review").DefinitionMetaName.Should().Be("Reviews");
        extended.Single(s => s.Name == "Review").DefinitionVersion.Should().Be(new Model.Version(3, 1));
        extended.Single(s => s.Name == "Orphan").DefinitionMetaName.Should().Be("missing-catalog");
        (await storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().ContainSingle();
        storage.SubscriptionStorage.RemoveAllUserTaskSubscriptionsByInstanceId(instance.InstanceId);
        (await storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().BeEmpty();
    }

    // Testzweck: Formulare werden versioniert abgelegt; das Loeschen der Metadaten entfernt alle
    // Versionen; die hoechste Version faellt ohne Bestand auf 0.0 zurueck.
    [Test]
    public async Task FormStorage_ShouldVersionFormsAndCascadeMetadataDeletion()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var formId = Guid.NewGuid();
        (await storage.FormStorage.GetMaxVersion(formId)).Should().Be(new Model.Version());
        await storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Approval" });
        await storage.FormStorage.SaveForm(new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0), FormData = "{}" });
        var latest = new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 1), FormData = "{\"v\":2}" };
        await storage.FormStorage.SaveForm(latest);

        (await storage.FormStorage.GetMaxVersion(formId)).Should().Be(new Model.Version(1, 1));
        (await storage.FormStorage.GetForm(latest.Id)).FormData.Should().Be("{\"v\":2}");
        (await storage.FormStorage.GetForms(formId)).Should().HaveCount(2);
        (await storage.FormStorage.GetFormMetadatas()).Should().ContainSingle().Which.Name.Should().Be("Approval");
        await storage.FormStorage.Invoking(s => s.GetFormMetaData(Guid.NewGuid())).Should().ThrowAsync<FileNotFoundException>();
        await storage.FormStorage.Invoking(s => s.UpdateFormMetaData(new FormMetadata { FormId = Guid.NewGuid(), Name = "x" })).Should().ThrowAsync<FileNotFoundException>();

        await storage.FormStorage.DeleteFormMetaData(formId);
        (await storage.FormStorage.GetForms(formId)).Should().BeEmpty();
        (await storage.FormStorage.GetFormMetadatas()).Should().BeEmpty();
    }

    // Testzweck: Die transaktionale Ablage macht Aenderungen erst mit CommitChanges sichtbar;
    // Entsorgen ohne Commit rollt zurueck. Das ist die Eigenschaft, die die Dateiablage nie hatte.
    [Test]
    public async Task TransactionalStorage_ShouldRollBackWithoutCommit_AndPersistWithCommit()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var rolledBack = CreateInstance(finished: false);
        var committed = CreateInstance(finished: false);

        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.InstanceStorage.AddOrUpdateInstance(rolledBack);
            (await storage.InstanceStorage.GetAllInstances()).Should().ContainSingle("the writer sees its own uncommitted row");
        }

        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.InstanceStorage.AddOrUpdateInstance(committed);
            storage.CommitChanges();
        }

        (await reader.InstanceStorage.GetAllInstances()).Select(i => i.InstanceId).Should().Equal(committed.InstanceId);
    }

    // Testzweck: Die Engine laeuft vollstaendig auf PostgreSQL: Deploy, parallele Starts und
    // parallele User-Task-Abschluesse enden mit beendeten Instanzen ohne offene Subscriptions.
    [Test]
    public async Task Engine_ShouldRunDeployStartAndCompleteOnPostgreSql()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var businessLogic = new BpmnBusinessLogic(provider);
        var definition = CreateDefinition("Definitions_Review", 1, 0, isActive: false);
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = definition.DefinitionId, Name = "Review" });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, UserTaskXml);
            storage.CommitChanges();
        }

        await businessLogic.DeployDefinition(definition);
        var instances = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => businessLogic.StartProcessInstance(definition.DefinitionId))));
        await Task.WhenAll(instances.Select(instance => Task.Run(async () =>
        {
            var token = instance.Tokens.Single(candidate => candidate.CurrentFlowNode is UserTask && candidate.State == FlowNodeState.Active);
            await businessLogic.HandleUserTask(new UserTaskResult { ProcessInstanceId = instance.InstanceId, TokenId = token.Id, FlowNodeId = "UserTask_Review" }, Guid.NewGuid());
        })));

        var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var stored = (await reader.InstanceStorage.GetAllInstances()).ToList();
        stored.Should().HaveCount(8);
        stored.Should().OnlyContain(instance => instance.State == ProcessInstanceState.Completed);
        (await reader.SubscriptionStorage.GetAllUserTasksExtended(Guid.NewGuid())).Should().BeEmpty();
        (await reader.DefinitionStorage.GetDeployedDefinition(definition.DefinitionId))!.Id.Should().Be(definition.Id);
    }

    // Testzweck: Doppelte Versionsnummern je Katalog bzw. Formular werden als Konflikt gemeldet, nicht gespeichert.
    [Test]
    public async Task DuplicateVersionNumbersAreRejectedAsConflicts()
    {
        // Zwei parallele Uploads, die dieselbe Versionsnummer berechnet haben, duerfen nicht beide landen.
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var first = CreateDefinition("catalog-dup", 1, 0, isActive: false);
        var second = CreateDefinition("catalog-dup", 1, 0, isActive: false);
        await storage.DefinitionStorage.StoreDefinition(first);

        var storeDuplicate = () => storage.DefinitionStorage.StoreDefinition(second);
        await storeDuplicate.Should().ThrowAsync<DefinitionStorageConflictException>();

        // Dieselbe Version erneut unter derselben Id ist eine Aktualisierung, kein Konflikt.
        await storage.DefinitionStorage.StoreDefinition(first);

        var formId = Guid.NewGuid();
        await storage.FormStorage.SaveForm(new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0), FormData = "{}" });
        var saveDuplicateForm = () => storage.FormStorage.SaveForm(new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0), FormData = "{}" });
        await saveDuplicateForm.Should().ThrowAsync<DefinitionStorageConflictException>();
    }

    private static BpmnDefinition CreateDefinition(string definitionId, int major, int minor, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        DefinitionId = definitionId,
        Hash = "hash",
        SavedByUser = Guid.NewGuid(),
        SavedOn = DateTime.UtcNow.AddMinutes(-major),
        Version = new Model.Version(major, minor),
        IsActive = isActive
    };

    private static ProcessInstanceInfo CreateInstance(bool finished)
    {
        var instanceId = Guid.NewGuid();
        var userTask = new UserTask { Id = "UserTask_1", Name = "Review", Implementation = "Approval" };
        var process = new Process { Id = "Process_1", Name = "P", DefinitionsId = "D", IsExecutable = true, FlowElements = [userTask] };
        var master = new Token { ProcessInstanceId = instanceId, CurrentBaseElement = process, ActiveBoundaryEvents = [], State = finished ? FlowNodeState.Completed : FlowNodeState.Active };
        var task = new Token { ProcessInstanceId = instanceId, ParentTokenId = master.Id, CurrentBaseElement = userTask, ActiveBoundaryEvents = [], State = finished ? FlowNodeState.Completed : FlowNodeState.Active };
        return new ProcessInstanceInfo
        {
            InstanceId = instanceId, metaDefinitionId = "catalog-1", DefinitionId = Guid.NewGuid(), ProcessId = "Process_1",
            Tokens = [master, task], IsFinished = finished, State = finished ? ProcessInstanceState.Completed : ProcessInstanceState.Waiting,
            MessageSubscriptionCount = 0, SignalSubscriptionCount = 0, UserTaskSubscriptionCount = finished ? 0 : 1, ServiceSubscriptionCount = 0
        };
    }

    private const string UserTaskXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_Review" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_Review" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1"><bpmn:outgoing>Flow_ToReview</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="Flow_ToReview" sourceRef="StartEvent_1" targetRef="UserTask_Review" />
            <bpmn:userTask id="UserTask_Review" name="Review">
              <bpmn:extensionElements><zeebe:formDefinition formKey="Approval" /></bpmn:extensionElements>
              <bpmn:incoming>Flow_ToReview</bpmn:incoming><bpmn:outgoing>Flow_ToEnd</bpmn:outgoing>
            </bpmn:userTask>
            <bpmn:sequenceFlow id="Flow_ToEnd" sourceRef="UserTask_Review" targetRef="EndEvent_1" />
            <bpmn:endEvent id="EndEvent_1"><bpmn:incoming>Flow_ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;
}
