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
    internal static string Xml(string reviewFormKey, bool withApprove, string secondNodeId = "Second") => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="Definitions_Migration" targetNamespace="test">
          <bpmn:process id="Process_Migration" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:exclusiveGateway id="Split" default="Flow_Right" />
            <bpmn:userTask id="Review" name="Review"><bpmn:extensionElements>
              <zeebe:formDefinition formKey="{{reviewFormKey}}" />
            </bpmn:extensionElements></bpmn:userTask>
            {{(withApprove ? """
              <bpmn:userTask id="Approve" name="Approve"><bpmn:extensionElements>
                <zeebe:formDefinition formKey="Approval" />
              </bpmn:extensionElements></bpmn:userTask>
              <bpmn:sequenceFlow id="Flow_Approve" sourceRef="Review" targetRef="Approve" />
              <bpmn:sequenceFlow id="Flow_LeftEnd" sourceRef="Approve" targetRef="EndLeft" />
              """ : """
              <bpmn:sequenceFlow id="Flow_LeftEnd" sourceRef="Review" targetRef="EndLeft" />
              """)}}
            <bpmn:userTask id="{{secondNodeId}}" name="{{secondNodeId}}"><bpmn:extensionElements>
              <zeebe:formDefinition formKey="Approval" />
            </bpmn:extensionElements></bpmn:userTask>
            <bpmn:endEvent id="EndLeft" />
            <bpmn:endEvent id="EndRight" />
            <bpmn:sequenceFlow id="Flow_Start" sourceRef="Start" targetRef="Split" />
            <bpmn:sequenceFlow id="Flow_Left" sourceRef="Split" targetRef="Review">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=path = "left"</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_Right" sourceRef="Split" targetRef="{{secondNodeId}}" />
            <bpmn:sequenceFlow id="Flow_RightEnd" sourceRef="{{secondNodeId}}" targetRef="EndRight" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static string ServiceXml(bool withApprove) => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="Definitions_Migration" targetNamespace="test">
          <bpmn:process id="Process_Migration" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:serviceTask id="Fetch" name="Fetch"><bpmn:extensionElements>
              <zeebe:taskDefinition type="fetch" retries="3" />
            </bpmn:extensionElements></bpmn:serviceTask>
            {{(withApprove ? """
              <bpmn:userTask id="Approve" name="Approve"><bpmn:extensionElements>
                <zeebe:formDefinition formKey="Approval" />
              </bpmn:extensionElements></bpmn:userTask>
              <bpmn:sequenceFlow id="Flow_Approve" sourceRef="Fetch" targetRef="Approve" />
              <bpmn:sequenceFlow id="Flow_End" sourceRef="Approve" targetRef="End" />
              """ : """
              <bpmn:sequenceFlow id="Flow_End" sourceRef="Fetch" targetRef="End" />
              """)}}
            <bpmn:endEvent id="End" />
            <bpmn:sequenceFlow id="Flow_Start" sourceRef="Start" targetRef="Fetch" />
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
