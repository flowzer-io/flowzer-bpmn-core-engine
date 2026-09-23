using BPMN.Common;
using BPMN.HumanInteraction;
using BPMN.Process;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>
/// Legt eine Instanz mit je einem Datensatz jeder instanzgebundenen Datenart an und prueft
/// anschliessend, dass davon nichts uebrig bleibt.
///
/// Bewusst gemeinsam fuer beide Ablagen: Was zu einer Instanz gehoert, ist eine fachliche
/// Aussage. Stuende die Liste zweimal getrennt im Test, koennte eine Ablage eine Datenart
/// stillschweigend liegen lassen und der zugehoerige Test bliebe trotzdem gruen.
/// </summary>
internal static class InstancePurgeFixture
{
    /// <summary>Was an einer Instanz haengt und mit ihr verschwinden muss.</summary>
    internal sealed record Seeded(
        Guid InstanceId,
        Guid UserTaskId,
        Guid TimerSubscriptionId,
        Guid ServiceTaskJobId,
        Guid RuntimeEventId,
        Guid AiRunId,
        Guid NotificationId,
        string IdempotencyScopeHash,
        string OwnerKey);

    internal const string MetaDefinitionId = "retention-catalog";

    /// <summary>
    /// Legt eine beendete Instanz samt allem Angehaengten an. <paramref name="finished"/> auf
    /// <c>false</c> erzeugt stattdessen eine laufende Instanz — dieselbe Datenlage, damit ein
    /// Test belegen kann, dass sie unberuehrt bleibt.
    /// </summary>
    internal static async Task<Seeded> SeedAsync(
        IStorageSystem storage,
        DateTime finishedAtUtc,
        bool finished = true,
        string? metaDefinitionId = null)
    {
        var relatedDefinitionId = metaDefinitionId ?? MetaDefinitionId;
        var instanceId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var ownerKey = new string('a', 64);

        var process = new Process
        {
            Id = "Process_Retention",
            Name = "Aufbewahrung",
            DefinitionsId = "Definitions_Retention",
            IsExecutable = true,
            FlowElements = []
        };
        var userTask = new UserTask { Id = "UserTask_1", Name = "Pruefen", Implementation = "Form" };

        // Der Zustandssetzer schreibt LastStateChangeTime auf „jetzt"; der Endzeitpunkt wird
        // deshalb danach gesetzt — genau so, wie die Ablage ihn beim Laden wiederherstellt.
        var token = new Token
        {
            ProcessInstanceId = instanceId,
            CurrentBaseElement = process,
            ActiveBoundaryEvents = [],
            State = finished ? FlowNodeState.Completed : FlowNodeState.Active
        };
        token.StartTime = finishedAtUtc.AddDays(-1);
        token.LastStateChangeTime = finishedAtUtc;

        await storage.InstanceStorage.AddOrUpdateInstance(new ProcessInstanceInfo
        {
            InstanceId = instanceId,
            metaDefinitionId = relatedDefinitionId,
            DefinitionId = definitionId,
            ProcessId = process.Id,
            Tokens = [token],
            IsFinished = finished,
            State = finished ? ProcessInstanceState.Completed : ProcessInstanceState.Waiting,
            MessageSubscriptionCount = 1,
            SignalSubscriptionCount = 1,
            UserTaskSubscriptionCount = 1,
            ServiceSubscriptionCount = 1,
            Migrations = [new InstanceMigrationRecord(Guid.NewGuid(), definitionId, finishedAtUtc, Guid.NewGuid())]
        });

        await storage.SubscriptionStorage.AddMessageSubscription(new MessageSubscription(
            new MessageDefinition { Name = "Antwort" },
            process.Id, relatedDefinitionId, definitionId, instanceId));

        storage.SubscriptionStorage.AddSignalSubscription(new SignalSubscription(
            "Signal_1", process.Id, relatedDefinitionId, definitionId, instanceId));

        var userTaskSubscription = new UserTaskSubscription
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
            MetaDefinitionId = relatedDefinitionId,
            DefinitionId = definitionId,
            ProcessId = process.Id
        };
        await storage.SubscriptionStorage.AddUserTaskSubscription(userTaskSubscription);

        await storage.UserTaskDraftStorage.TrySave(new UserTaskDraft
        {
            UserTaskId = userTaskSubscription.Id,
            OwnerKey = ownerKey,
            OwnerUserId = Guid.NewGuid(),
            TokenId = userTaskSubscription.Token.Id,
            ProcessInstanceId = instanceId,
            DefinitionId = definitionId,
            Revision = 1,
            UpdatedAtUtc = finishedAtUtc,
            DataJson = """{"value":"entwurf"}"""
        }, 0);

        var assignmentEvent = new UserTaskAssignmentEvent
        {
            Id = Guid.NewGuid(),
            UserTaskId = userTaskSubscription.Id,
            ProcessInstanceId = instanceId,
            DefinitionId = definitionId,
            ProcessId = process.Id,
            FlowNodeId = userTask.Id,
            Revision = 1,
            Action = "claim",
            ActorOwnerKey = ownerKey,
            ActorUserId = Guid.NewGuid(),
            Reason = "test",
            CorrelationId = "correlation-1",
            OccurredAtUtc = finishedAtUtc
        };
        await storage.UserTaskLifecycleStorage.TryWrite(
            new UserTaskWorkState
            {
                UserTaskId = userTaskSubscription.Id,
                Revision = 1,
                AssigneeOwnerKey = ownerKey,
                AssigneeUserId = assignmentEvent.ActorUserId,
                UpdatedAtUtc = finishedAtUtc
            },
            0,
            assignmentEvent);

        await storage.UserTaskDeadlineStorage.AddIfAbsent(new UserTaskDeadline
        {
            UserTaskId = userTaskSubscription.Id,
            Revision = 1,
            ActivatedAtUtc = finishedAtUtc,
            ScheduleState = "resolved",
            Status = "scheduled",
            DueAtUtc = finishedAtUtc.AddDays(1),
            PolicyVersion = "test-v1",
            UpdatedAtUtc = finishedAtUtc
        });

        var notification = new UserTaskNotification
        {
            Id = Guid.NewGuid(),
            UserTaskId = userTaskSubscription.Id,
            Kind = "due",
            DeduplicationKey = $"due:{userTaskSubscription.Id:N}",
            OccurredAtUtc = finishedAtUtc
        };
        await storage.UserTaskNotificationStorage.TryAdd(notification);

        var timerSubscription = new TimerSubscription
        {
            DueAt = finishedAtUtc.AddDays(1),
            FlowNodeId = "Timer_1",
            Kind = TimerSubscriptionKind.IntermediateCatchEvent,
            ProcessId = process.Id,
            RelatedDefinitionId = relatedDefinitionId,
            DefinitionId = definitionId,
            ProcessInstanceId = instanceId,
            TokenId = token.Id
        };
        await storage.SubscriptionStorage.AddTimerSubscription(timerSubscription);

        var job = new ServiceTaskJob
        {
            Id = Guid.NewGuid(),
            Type = "retention-worker",
            Name = "Auftrag",
            TokenId = Guid.NewGuid(),
            FlowNodeId = "ServiceTask_1",
            ProcessInstanceId = instanceId,
            MetaDefinitionId = relatedDefinitionId,
            DefinitionId = definitionId,
            ProcessId = process.Id,
            CreatedAt = finishedAtUtc
        };
        await storage.ServiceTaskStorage.SaveJob(job);

        var runtimeEvent = new RuntimeNodeEvent
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = instanceId,
            DefinitionId = definitionId,
            TokenId = token.Id,
            FlowNodeId = process.Id,
            State = FlowNodeState.Completed,
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = new DateTimeOffset(DateTime.SpecifyKind(finishedAtUtc, DateTimeKind.Utc))
        };
        await storage.RuntimeNodeEventStorage.AppendIfAbsent(runtimeEvent);

        var aiRun = CreateAiRun(instanceId, definitionId, relatedDefinitionId, process.Id, finishedAtUtc);
        await storage.AiRunStorage.TryCreate(aiRun);

        // Der Scope-Hash ist ein Primaerschluessel. Zwei Instanzen derselben Testlage duerfen
        // sich denselben Wert nicht teilen, sonst ueberschriebe die zweite den Verweis der ersten.
        var scopeHash = instanceId.ToString("N") + new string('b', 32);
        await storage.IdempotencyStorage.TryCreate(new IdempotencyRecord
        {
            ScopeHash = scopeHash,
            RequestHash = instanceId.ToString("N") + new string('c', 32),
            Operation = "start-instance",
            CreatedAt = DateTime.SpecifyKind(finishedAtUtc, DateTimeKind.Utc),
            ExpiresAt = DateTime.SpecifyKind(finishedAtUtc.AddDays(1), DateTimeKind.Utc)
        });
        await storage.IdempotencyStorage.Complete(scopeHash, instanceId);

        return new Seeded(
            instanceId, userTaskSubscription.Id, timerSubscription.Id, job.Id,
            runtimeEvent.Id, aiRun.Id, notification.Id, scopeHash, ownerKey);
    }

    /// <summary>
    /// Belegt Datenart fuer Datenart, dass nichts von der Instanz uebrig ist. Jede Zusicherung
    /// steht einzeln da: Eine Sammelpruefung verschwiege, welche Datenart liegen geblieben ist.
    /// </summary>
    internal static async Task AssertPurgedAsync(IStorageSystem storage, Seeded seeded)
    {
        var instance = async () => await storage.InstanceStorage.GetProcessInstance(seeded.InstanceId);
        await instance.Should().ThrowAsync<FileNotFoundException>("die Instanz selbst ist weg");

        (await storage.SubscriptionStorage.GetMessageSubscription(seeded.InstanceId))
            .Should().BeEmpty("Nachrichtenanmeldungen gehen mit");
        (await storage.SubscriptionStorage.GetSignalSubscriptions(seeded.InstanceId))
            .Should().BeEmpty("Signalanmeldungen gehen mit");
        // GetAllUserTasks hydriert gegen die Tokens der Instanz und liefert nach deren Loeschung
        // ohnehin nichts mehr — als Beleg taugt das nicht. LockTask fragt die Anmeldung selbst
        // ab und ist in beiden Ablagen genau dann falsch, wenn es sie nicht mehr gibt.
        (await storage.UserTaskLifecycleStorage.LockTask(seeded.UserTaskId))
            .Should().BeFalse("die Benutzeraufgaben-Anmeldung selbst ist weg");
        (await storage.SubscriptionStorage.GetAllUserTasks(seeded.InstanceId))
            .Should().BeEmpty("ueber die Instanz ist keine Aufgabe mehr erreichbar");
        (await storage.SubscriptionStorage.GetTimerSubscriptions(seeded.InstanceId))
            .Should().BeEmpty("Timeranmeldungen gehen mit");

        (await storage.UserTaskDraftStorage.Get(seeded.UserTaskId, seeded.OwnerKey))
            .Should().BeNull("Aufgabenentwuerfe gehen mit");
        (await storage.UserTaskLifecycleStorage.Get(seeded.UserTaskId))
            .Should().BeNull("der Bearbeiterzustand geht mit");
        (await storage.UserTaskLifecycleStorage.GetEventsByProcessInstance(seeded.InstanceId))
            .Should().BeEmpty("die Human-Task-Historie geht mit");
        (await storage.UserTaskDeadlineStorage.Get(seeded.UserTaskId))
            .Should().BeNull("Faelligkeiten gehen mit");
        (await storage.UserTaskNotificationStorage.Get(seeded.NotificationId))
            .Should().BeNull("Aufgabenmeldungen gehen mit");

        (await storage.ServiceTaskStorage.GetJob(seeded.ServiceTaskJobId))
            .Should().BeNull("Auftraege an Worker gehen mit");
        (await storage.RuntimeNodeEventStorage.GetByProcessInstance(seeded.InstanceId))
            .Should().BeEmpty("die Engine-Ereignisspur geht mit");
        (await storage.AiRunStorage.Get(seeded.AiRunId))
            .Should().BeNull("KI-Laeufe gehen mit");
        (await storage.IdempotencyStorage.Get(seeded.IdempotencyScopeHash))
            .Should().BeNull("Idempotenzschluessel auf die Instanz gehen mit");
    }

    /// <summary>Belegt, dass alle Datenarten dieser Instanz noch vollstaendig da sind.</summary>
    internal static async Task AssertIntactAsync(IStorageSystem storage, Seeded seeded)
    {
        (await storage.InstanceStorage.GetProcessInstance(seeded.InstanceId)).Should().NotBeNull();
        (await storage.SubscriptionStorage.GetMessageSubscription(seeded.InstanceId)).Should().ContainSingle();
        (await storage.SubscriptionStorage.GetSignalSubscriptions(seeded.InstanceId)).Should().ContainSingle();
        (await storage.UserTaskLifecycleStorage.LockTask(seeded.UserTaskId)).Should().BeTrue();
        (await storage.SubscriptionStorage.GetTimerSubscriptions(seeded.InstanceId)).Should().ContainSingle();
        (await storage.UserTaskDraftStorage.Get(seeded.UserTaskId, seeded.OwnerKey)).Should().NotBeNull();
        (await storage.UserTaskLifecycleStorage.Get(seeded.UserTaskId)).Should().NotBeNull();
        (await storage.UserTaskLifecycleStorage.GetEventsByProcessInstance(seeded.InstanceId)).Should().ContainSingle();
        (await storage.UserTaskDeadlineStorage.Get(seeded.UserTaskId)).Should().NotBeNull();
        (await storage.UserTaskNotificationStorage.Get(seeded.NotificationId)).Should().NotBeNull();
        (await storage.ServiceTaskStorage.GetJob(seeded.ServiceTaskJobId)).Should().NotBeNull();
        (await storage.RuntimeNodeEventStorage.GetByProcessInstance(seeded.InstanceId)).Should().ContainSingle();
        (await storage.AiRunStorage.Get(seeded.AiRunId)).Should().NotBeNull();
        (await storage.IdempotencyStorage.Get(seeded.IdempotencyScopeHash)).Should().NotBeNull();
    }

    private static AiRun CreateAiRun(
        Guid instanceId, Guid definitionId, string relatedDefinitionId, string processId, DateTime atUtc)
    {
        var utc = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
        return new AiRun
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = instanceId,
            TokenId = Guid.NewGuid(),
            FlowNodeId = "AiTask_1",
            MetaDefinitionId = relatedDefinitionId,
            DefinitionId = definitionId,
            ProcessId = processId,
            ConnectionId = Guid.NewGuid(),
            ConnectionRevision = 1,
            Model = "test-model",
            InstructionVersion = 1,
            Instruction = "Fasse zusammen.",
            InputsJson = """{"text":"eingabe"}""",
            ResultSchema = """{"type":"object","properties":{}}""",
            MaxInputTokens = 1000,
            MaxOutputTokens = 1000,
            TimeoutSeconds = 30,
            MaximumAttempts = 3,
            // Ein neuer Lauf muss laut AiRunStorageRules.ValidateNew unangetastet sein.
            Status = AiRunStatus.Pending,
            Attempt = 0,
            Revision = 1,
            CreatedAtUtc = utc,
            UpdatedAtUtc = utc
        };
    }
}
