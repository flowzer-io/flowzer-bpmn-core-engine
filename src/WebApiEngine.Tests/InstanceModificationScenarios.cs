using System.Dynamic;
using core_engine;
using Flowzer.Shared;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

/// <summary>
/// Identische Eingriffsfaelle fuer isolierte Datei- und PostgreSQL-Ablage. Die Szenarien
/// arbeiten ausschliesslich ueber die echte Geschaeftslogik und die echte Ablage; der
/// HTTP-Vertrag liegt in <see cref="InstanceModificationIntegrationTest"/>.
/// </summary>
internal static class InstanceModificationScenarios
{
    internal const string MetaDefinitionId = "Definitions_Modification";
    internal const string Form = "Approval";

    /// <summary>
    /// Der Kernfall: Ein wartender Schritt wird verschoben. Die alte Aufgabe verschwindet, am
    /// Ziel entsteht eine neue mit <em>neuer</em> Kennung — genau der Unterschied zur Migration —,
    /// und die Spur haelt fest, was passiert ist, ohne einen einzigen Wert zu verewigen.
    /// </summary>
    internal static async Task CoreAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, Xml());
        var instance = await StartAsync(engine);
        var review = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        review.Token.CurrentFlowNode!.Id.Should().Be("Review");

        var actor = Guid.NewGuid();
        var request = new InstanceModificationRequest(
            [new InstanceModificationMove(review.Token.Id, "Approve")],
            new Dictionary<string, object?> { ["Betrag"] = 99, ["Kommentar"] = "streng-vertraulich" },
            ["Vermerk"]);

        var preview = await engine.PreviewInstanceModification(instance.InstanceId, request);
        using (new AssertionScope())
        {
            preview.Status.Should().Be(InstanceModificationRequestStatus.Accepted);
            preview.Applicable.Should().BeTrue();
            preview.Problems.Should().BeEmpty();
            preview.Notices.Select(notice => notice.Code).Should().Contain(
                nameof(InstanceModificationNoticeCode.UserTaskCancelled));
            preview.Steps.Should().ContainSingle().Which.Should().BeEquivalentTo(
                new { TokenId = review.Token.Id, FlowNodeId = "Review", Type = "UserTask" });
            // Start- und Boundary-Events stehen nie zur Wahl.
            preview.Targets.Select(target => target.Id).Should()
                .Contain(["Approve", "Notify", "EndApproved", "EndRejected"])
                .And.NotContain("Start");
        }

        // Der Trockenlauf ist eine Auskunft: Die Aufgabe steht danach unveraendert da.
        (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.Id.Should().Be(review.Id);

        var outcome = await engine.ModifyInstance(instance.InstanceId, request, actor);
        outcome.Status.Should().Be(InstanceModificationRequestStatus.Accepted);
        outcome.Modified.Should().BeTrue();

        var moved = (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        var stored = await InstanceAsync(provider, instance.InstanceId);
        using (new AssertionScope())
        {
            moved.Token.CurrentFlowNode!.Id.Should().Be("Approve");
            moved.Id.Should().NotBe(review.Id, "the old task is gone and a new one begins");
            moved.Token.Id.Should().NotBe(review.Token.Id);

            stored.Tokens.Single(token => token.Id == review.Token.Id).State
                .Should().Be(FlowNodeState.Withdrawn);
            stored.State.Should().Be(ProcessInstanceState.Waiting);

            var record = stored.Modifications.Should().ContainSingle().Subject;
            record.ModifiedByUserId.Should().Be(actor);
            record.Moves.Should().ContainSingle().Which.Should().Be(
                new InstanceModificationMoveRecord(review.Token.Id, "Review", "Approve"));
            record.VariablesSet.Should().Equal("Betrag", "Kommentar");
            record.VariablesRemoved.Should().Equal("Vermerk");
        }

        // Die Spur darf keine Werte tragen; sie ist fuer jeden lesbar, der die Instanz
        // inspizieren darf, und wuerde Fachdaten sonst dauerhaft verewigen.
        var trace = Newtonsoft.Json.JsonConvert.SerializeObject(stored.Modifications);
        using (new AssertionScope())
        {
            trace.Should().Contain("Kommentar", "the name of a changed variable belongs in the trace");
            trace.Should().NotContain("vertraulich").And.NotContain("geprüft");
        }

        // Die korrigierte Variable steht wirklich im Prozess, und die entfernte ist fort.
        var processVariables = stored.Tokens.Single(token => token.ParentTokenId == null).Variables;
        using (new AssertionScope())
        {
            processVariables.GetValue<long>("Betrag").Should().Be(99);
            processVariables.HasProperty("Vermerk").Should().BeFalse();
        }

        // Der Beweis, dass die Instanz wirklich am Ziel weiterlaeuft.
        await CompleteAsync(new BpmnBusinessLogic(provider), moved);
        (await JobsAsync(provider, instance.InstanceId)).Should().ContainSingle()
            .Which.FlowNodeId.Should().Be("Notify");

        // Der naechste gewoehnliche Schreibvorgang darf die Spur nicht stillschweigend loeschen.
        (await InstanceAsync(provider, instance.InstanceId)).Modifications.Should().ContainSingle();
    }

    /// <summary>
    /// Ein wartender Service-Task: Sein Auftrag verfaellt mit dem Eingriff. Der Trockenlauf muss
    /// das ankuendigen, sonst wartet ein Worker auf Arbeit, die es nicht mehr gibt.
    /// </summary>
    internal static async Task ServiceTaskJobAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, Xml());
        var instance = await StartAsync(engine);
        var review = (await TasksAsync(provider, instance.InstanceId)).Single();
        await CompleteAsync(engine, review);
        var approve = (await TasksAsync(provider, instance.InstanceId)).Single();
        await CompleteAsync(new BpmnBusinessLogic(provider), approve);

        var job = (await JobsAsync(provider, instance.InstanceId)).Should().ContainSingle().Subject;
        job.FlowNodeId.Should().Be("Notify");

        var request = new InstanceModificationRequest([new InstanceModificationMove(job.TokenId, "EndRejected")]);

        var preview = await engine.PreviewInstanceModification(instance.InstanceId, request);
        using (new AssertionScope())
        {
            preview.Applicable.Should().BeTrue();
            preview.Notices.Should().ContainSingle().Which.Should().Match<InstanceModificationFinding>(notice =>
                notice.Code == nameof(InstanceModificationNoticeCode.ServiceTaskJobCancelled)
                && notice.FlowNodeId == "Notify");
        }

        // Der Trockenlauf laesst den Auftrag unangetastet.
        (await JobsAsync(provider, instance.InstanceId)).Should().ContainSingle().Which.Id.Should().Be(job.Id);

        (await new BpmnBusinessLogic(provider).ModifyInstance(instance.InstanceId, request, Guid.NewGuid()))
            .Modified.Should().BeTrue();

        using (new AssertionScope())
        {
            (await JobsAsync(provider, instance.InstanceId)).Should().BeEmpty(
                "the job of a withdrawn service task is gone");
            (await InstanceAsync(provider, instance.InstanceId)).State
                .Should().Be(ProcessInstanceState.Completed, "the target is an end event");
        }
    }

    /// <summary>
    /// Ein privater Entwurf geht mit der verschwindenden Aufgabe verloren. Anders als bei der
    /// Migration gibt es hier keine Bedingung: Die Aufgabe ist weg, also ist der Entwurf es auch.
    /// Der Trockenlauf muss das vorher sagen.
    /// </summary>
    internal static async Task DraftAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, Xml());
        var instance = await StartAsync(engine);
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

        var request = new InstanceModificationRequest([new InstanceModificationMove(review.Token.Id, "Approve")]);

        (await engine.PreviewInstanceModification(instance.InstanceId, request))
            .Notices.Select(notice => notice.Code).Should().Contain(
                InstanceModificationCodes.UserTaskDraftDiscarded);

        (await new BpmnBusinessLogic(provider).ModifyInstance(instance.InstanceId, request, Guid.NewGuid()))
            .Modified.Should().BeTrue();

        using var reader = provider.GetTransactionalStorage();
        (await reader.UserTaskDraftStorage.Get(review.Id, ownerKey)).Should().BeNull(
            "the task is gone, so its drafts are too");
    }

    /// <summary>
    /// Hindernisse tragen stabile Codes, und eine abgelehnte Anfrage veraendert nichts. Genau
    /// darauf verlaesst sich die Oberflaeche, wenn sie den Trockenlauf anzeigt.
    /// </summary>
    internal static async Task ProblemsAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, Xml());
        var instance = await StartAsync(engine);
        var review = (await TasksAsync(provider, instance.InstanceId)).Single();

        var request = new InstanceModificationRequest([new InstanceModificationMove(review.Token.Id, "Start")]);

        var preview = await engine.PreviewInstanceModification(instance.InstanceId, request);
        using (new AssertionScope())
        {
            preview.Applicable.Should().BeFalse();
            preview.Problems.Should().ContainSingle()
                .Which.Code.Should().Be(nameof(InstanceModificationProblemCode.TargetNotAllowed));
        }

        var outcome = await new BpmnBusinessLogic(provider)
            .ModifyInstance(instance.InstanceId, request, Guid.NewGuid());
        using (new AssertionScope())
        {
            outcome.Status.Should().Be(InstanceModificationRequestStatus.NotApplicable);
            outcome.Modified.Should().BeFalse();
            outcome.Problems.Should().ContainSingle()
                .Which.Code.Should().Be(nameof(InstanceModificationProblemCode.TargetNotAllowed));
        }

        using (new AssertionScope())
        {
            (await TasksAsync(provider, instance.InstanceId)).Should().ContainSingle()
                .Which.Id.Should().Be(review.Id);
            (await InstanceAsync(provider, instance.InstanceId)).Modifications.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Eine beendete Instanz ist ein Zustandskonflikt, keine unbrauchbare Angabe — der
    /// Controller macht daraus 409 statt 422.
    /// </summary>
    internal static async Task FinishedInstanceAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, Xml());
        var instance = await StartAsync(engine);
        var review = (await TasksAsync(provider, instance.InstanceId)).Single();
        var tokenId = review.Token.Id;
        await new BpmnBusinessLogic(provider).CancelInstance(instance.InstanceId);

        var request = new InstanceModificationRequest([new InstanceModificationMove(tokenId, "Approve")]);

        using (new AssertionScope())
        {
            (await new BpmnBusinessLogic(provider).PreviewInstanceModification(instance.InstanceId, request))
                .Status.Should().Be(InstanceModificationRequestStatus.InstanceNotRunning);
            (await new BpmnBusinessLogic(provider).ModifyInstance(instance.InstanceId, request, Guid.NewGuid()))
                .Status.Should().Be(InstanceModificationRequestStatus.InstanceNotRunning);
        }
    }

    /// <summary>
    /// Eine leere Anfrage ist der Weg, ueberhaupt erst zu erfahren, was moeglich ist. Als
    /// Trockenlauf gilt sie, als Eingriff nicht — sonst schriebe ein Klick ohne Auswahl eine
    /// Spur ohne Inhalt.
    /// </summary>
    internal static async Task EmptyRequestAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        await DeployAsync(provider, engine, Xml());
        var instance = await StartAsync(engine);

        var preview = await engine.PreviewInstanceModification(
            instance.InstanceId, new InstanceModificationRequest());
        using (new AssertionScope())
        {
            preview.Status.Should().Be(InstanceModificationRequestStatus.Accepted);
            preview.Applicable.Should().BeTrue();
            preview.Steps.Should().ContainSingle().Which.FlowNodeId.Should().Be("Review");
            preview.Targets.Should().NotBeEmpty();
        }

        (await new BpmnBusinessLogic(provider)
                .ModifyInstance(instance.InstanceId, new InstanceModificationRequest(), Guid.NewGuid()))
            .Status.Should().Be(InstanceModificationRequestStatus.NothingToDo);
    }

    /// <summary>Eine Instanz, die es nicht gibt, ist keine 422, sondern eine 404.</summary>
    internal static async Task UnknownInstanceAsync(ITransactionalStorageProvider provider)
    {
        var engine = new BpmnBusinessLogic(provider);
        var request = new InstanceModificationRequest([new InstanceModificationMove(Guid.NewGuid(), "Approve")]);

        using (new AssertionScope())
        {
            (await engine.PreviewInstanceModification(Guid.NewGuid(), request))
                .Status.Should().Be(InstanceModificationRequestStatus.UnknownInstance);
            (await engine.ModifyInstance(Guid.NewGuid(), request, Guid.NewGuid()))
                .Status.Should().Be(InstanceModificationRequestStatus.UnknownInstance);
        }
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

    internal static async Task<ServiceTaskJob[]> JobsAsync(
        ITransactionalStorageProvider provider, Guid instanceId)
    {
        using var storage = provider.GetTransactionalStorage();
        return (await storage.ServiceTaskStorage.GetJobs())
            .Where(job => job.ProcessInstanceId == instanceId)
            .ToArray();
    }

    internal static async Task<ProcessInstanceInfo> StartAsync(
        BpmnBusinessLogic engine, string? metaDefinitionId = null)
    {
        dynamic variables = new ExpandoObject();
        variables.Betrag = 10;
        variables.Vermerk = "geprüft";
        return await engine.StartProcessInstance(
            metaDefinitionId ?? MetaDefinitionId, (ExpandoObject)variables);
    }

    internal static async Task CompleteAsync(BpmnBusinessLogic engine, UserTaskSubscription task)
    {
        (await engine.CompleteUserTaskAsync(new UserTaskResult
        {
            ProcessInstanceId = task.ProcessInstanceId,
            TokenId = task.Token.Id,
            FlowNodeId = task.Token.CurrentFlowNode!.Id,
            Data = new ExpandoObject()
        }, new WebApiEngine.Auth.CurrentUserContext(Guid.NewGuid(), "modification-test", false),
            canOperateAllTasks: true))
            .Should().Be(UserTaskCompletionOutcome.Completed);
    }

    internal static async Task<BpmnDefinition> DeployAsync(
        ITransactionalStorageProvider provider,
        BpmnBusinessLogic engine,
        string xml,
        string? metaDefinitionId = null)
    {
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(), DefinitionId = metaDefinitionId ?? MetaDefinitionId,
            Version = new Model.Version(1, 0), Hash = "test",
            SavedByUser = Guid.NewGuid(), SavedOn = DateTime.UtcNow, IsActive = false
        };
        using (var storage = provider.GetTransactionalStorage())
        {
            // Der Katalogeintrag darf nur einmal entstehen; PostgreSQL meldet sonst einen Konflikt.
            if ((await storage.DefinitionStorage.GetAllMetaDefinitions())
                .All(meta => meta.DefinitionId != definition.DefinitionId))
                await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                {
                    DefinitionId = definition.DefinitionId, Name = "Modification"
                });
            await FormTestSeed.StoreAsync(storage, Form, """{"components":[{"type":"textfield","key":"answer"}]}""");
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
            storage.CommitChanges();
        }

        await engine.DeployDefinition(definition);
        return definition;
    }

    /// <summary>
    /// Zwei Aufgaben, ein Gateway, ein Service-Task und zwei Endereignisse: genug, um jeden
    /// Zieltyp des Eingriffs anzufahren, ohne das Modell aufzublaehen. Der Ablehnungszweig
    /// haengt bewusst am Gateway — ein Knoten ohne eingehenden Fluss liesse sich gar nicht
    /// deployen, und genau solche Modelle sind hier auch nicht der Fall.
    /// </summary>
    internal static string Xml() => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            id="{{MetaDefinitionId}}" targetNamespace="test">
          <bpmn:process id="Process_Modification" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:userTask id="Review" name="Prüfung"><bpmn:extensionElements>
              <zeebe:formDefinition formKey="{{Form}}" />
            </bpmn:extensionElements></bpmn:userTask>
            <bpmn:exclusiveGateway id="Split" name="Entscheidung" default="Flow_Reject" />
            <bpmn:userTask id="Approve" name="Freigabe"><bpmn:extensionElements>
              <zeebe:formDefinition formKey="{{Form}}" />
            </bpmn:extensionElements></bpmn:userTask>
            <bpmn:serviceTask id="Notify" name="Benachrichtigen"><bpmn:extensionElements>
              <zeebe:taskDefinition type="notify" retries="3" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="EndApproved" />
            <bpmn:endEvent id="EndRejected" />
            <bpmn:sequenceFlow id="Flow_Start" sourceRef="Start" targetRef="Review" />
            <bpmn:sequenceFlow id="Flow_Review" sourceRef="Review" targetRef="Split" />
            <bpmn:sequenceFlow id="Flow_Split" sourceRef="Split" targetRef="Approve">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=Betrag &gt; 0</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_Reject" sourceRef="Split" targetRef="EndRejected" />
            <bpmn:sequenceFlow id="Flow_Approve" sourceRef="Approve" targetRef="Notify" />
            <bpmn:sequenceFlow id="Flow_Notify" sourceRef="Notify" targetRef="EndApproved" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
