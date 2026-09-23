using System.Dynamic;
using BPMN.Common;
using BPMN.Flowzer.Events;
using BPMN.HumanInteraction;
using BPMN.Process;
using Flowzer.Shared;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

public class InstanceMigrationTest
{
    // Testzweck: Prüft den vollständigen Umzug einer wartenden Instanz auf ein deckungsgleiches
    // Zielmodell inklusive erhaltener Kennungen, Zeitstempel und Variablen.
    [Test]
    public async Task MigratesWaitingInstanceOntoTargetProcess()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReview_v2.bpmn");

        var waitingToken = instance.GetActiveUserTasks().Single();
        var sourceTokens = instance.Tokens.ToArray();
        var finishedTokens = sourceTokens.Where(token => !token.IsAlive()).ToArray();
        var startTime = waitingToken.StartTime;
        var lastStateChangeTime = waitingToken.LastStateChangeTime;

        var plan = InstanceMigration.Plan(sourceTokens, targetProcess);

        using (new AssertionScope())
        {
            plan.Problems.Should().BeEmpty();
            plan.IsMigratable.Should().BeTrue();
            plan.WaitingTokens.Should().ContainSingle().Which.Id.Should().Be(waitingToken.Id);
            plan.TargetFlowNodeOf(waitingToken.Id).Should().BeOfType<UserTask>()
                .Which.Implementation.Should().Be("review-form-v2");
        }

        var migratedTokens = InstanceMigration.Apply(plan, Helper.TestFlowzerConfig);

        var migratedMaster = migratedTokens.Single(token => token.ParentTokenId == null);
        var migratedWaitingToken = migratedTokens.Single(token => token.Id == waitingToken.Id);

        using (new AssertionScope())
        {
            migratedTokens.Select(token => token.Id).Should().Equal(sourceTokens.Select(token => token.Id));
            migratedMaster.CurrentBaseElement.Should().BeSameAs(targetProcess);
            migratedMaster.Variables.GetValue<string>("Antragsteller").Should().Be("Lukas");
            migratedWaitingToken.State.Should().Be(FlowNodeState.Active);
            migratedWaitingToken.StartTime.Should().Be(startTime);
            migratedWaitingToken.LastStateChangeTime.Should().Be(lastStateChangeTime);
            // Das Element stammt wirklich aus dem Zielmodell: Nur dort trägt die Aufgabe das neue Formular.
            migratedWaitingToken.CurrentFlowNode!.Id.Should().Be("UserTask_Review");
            migratedWaitingToken.CurrentFlowNode.Should().BeOfType<UserTask>()
                .Which.Implementation.Should().Be("review-form-v2");

            // Abgeschlossene Tokens sind Historie und werden unverändert übernommen.
            foreach (var finishedToken in finishedTokens)
            {
                migratedTokens.Single(token => token.Id == finishedToken.Id).Should().BeSameAs(finishedToken);
            }

            // Die Eingabe bleibt unangetastet, damit ein abgelehnter Umzug nichts hinterlässt.
            instance.MasterToken.CurrentBaseElement.Should().NotBeSameAs(targetProcess);
            waitingToken.CurrentFlowNode!.Should().BeOfType<UserTask>()
                .Which.Implementation.Should().Be("review-form");
            waitingToken.State.Should().Be(FlowNodeState.Active);
            waitingToken.LastStateChangeTime.Should().Be(lastStateChangeTime);
            migratedWaitingToken.Should().NotBeSameAs(waitingToken);
        }

        var migratedInstance = new InstanceEngine(migratedTokens, Helper.TestFlowzerConfig);
        migratedInstance.HandleTaskResult(waitingToken.Id, new ExpandoObject());

        // Der Beweis, dass wirklich das Zielmodell läuft: Die neue Aufgabe gibt es nur dort.
        migratedInstance.GetActiveUserTasks().Single().CurrentFlowNode!.Id.Should().Be("UserTask_Approve");
    }

    // Testzweck: Prüft, dass ein im Zielmodell fehlender FlowNode den Umzug verhindert.
    [Test]
    public async Task ReportsMissingFlowNode()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewRenamed_v2.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.FlowNodeMissing);
            problem.FlowNodeId.Should().Be("UserTask_Review");
        }
    }

    // Testzweck: Prüft, dass ein Typwechsel desselben FlowNodes den Umzug verhindert.
    [Test]
    public async Task ReportsChangedFlowNodeType()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewAsService_v2.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.FlowNodeTypeChanged);
            problem.FlowNodeId.Should().Be("UserTask_Review");
        }
    }

    // Testzweck: Prüft, dass ein Zielmodell mit anderer Prozesskennung abgelehnt wird.
    [Test]
    public async Task ReportsChangedProcess()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewOtherProcess_v2.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.ProcessChanged);
            problem.FlowNodeId.Should().BeNull();
        }
    }

    // Testzweck: Prüft, dass eine beendete Instanz ausschließlich als nicht laufend gemeldet wird.
    [Test]
    public async Task ReportsFinishedInstanceAsNotRunning()
    {
        var instance = await StartReviewInstance();
        instance.HandleTaskResult(instance.GetActiveUserTasks().Single().Id, new ExpandoObject());
        instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);

        var targetProcess = await LoadProcess("MigrationReview_v2.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            plan.Problems.Should().ContainSingle()
                .Which.Code.Should().Be(InstanceMigrationProblemCode.InstanceNotRunning);
            InstanceMigration.Plan([], targetProcess).Problems.Should().ContainSingle()
                .Which.Code.Should().Be(InstanceMigrationProblemCode.InstanceNotRunning);
        }
    }

    // Testzweck: Prüft, dass ein geänderter ServiceTask-Typ den Umzug verhindert, weil ein
    // eingereihter Auftrag sonst den falschen Typ trüge.
    [Test]
    public async Task ReportsChangedServiceTaskType()
    {
        var instance = await Helper.StartFirstProcessOfFile("MigrationService_v1.bpmn");
        var targetProcess = await LoadProcess("MigrationService_v2.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.ServiceTaskTypeChanged);
            problem.FlowNodeId.Should().Be("ServiceTask_Fetch");
        }
    }

    // Testzweck: Prüft, dass eine KI-Aufgabe den Umzug verhindert, weil ihr Vertrag eigenen
    // Regeln folgt und nicht stillschweigend ausgetauscht werden darf.
    [Test]
    public async Task ReportsAiTaskAsNotSupported()
    {
        var instance = await Helper.StartFirstProcessOfFile("MigrationAi_v1.bpmn");
        var targetProcess = await LoadProcess("MigrationAi_v2.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.AiTaskNotSupported);
            problem.FlowNodeId.Should().Be("ServiceTask_Classify");
        }
    }

    // Testzweck: Prüft, dass ein lebender Token, der nicht ruht, den Umzug verhindert.
    [Test]
    public async Task ReportsTokenThatIsNotAtRest()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReview_v2.bpmn");
        var waitingToken = instance.GetActiveUserTasks().Single();

        // Ein mitten im Lauf eingefrorener Token kommt in der Ablage nicht vor und wird deshalb
        // hier von Hand gebaut.
        var tokens = instance.Tokens
            .Select(token => token.Id == waitingToken.Id ? WithState(token, FlowNodeState.Ready) : token)
            .ToArray();

        var plan = InstanceMigration.Plan(tokens, targetProcess);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.TokenNotAtRest);
            problem.FlowNodeId.Should().Be("UserTask_Review");
        }
    }

    // Testzweck: Prüft, dass Multi-Instance-Tokens in der ersten Stufe abgelehnt werden.
    [Test]
    public async Task ReportsMultiInstanceAsNotSupported()
    {
        var instance = await Helper.StartFirstProcessOfFile("ParallelFlowTest.bpmn");
        var targetProcess = await LoadProcess("ParallelFlowTest.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            plan.Problems.Should().NotBeEmpty();
            plan.Problems.Should().OnlyContain(problem =>
                problem.Code == InstanceMigrationProblemCode.MultiInstanceNotSupported);
        }
    }

    // Testzweck: Prüft, dass Tokens in einem Subprozess-Scope in der ersten Stufe abgelehnt werden.
    [Test]
    public async Task ReportsSubProcessAsNotSupported()
    {
        var process = await LoadProcess("SubProcess.bpmn");
        var instance = Helper.CreateProcessEngine(process).StartProcess();

        var plan = InstanceMigration.Plan(instance.Tokens, process);

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            plan.Problems.Should().NotBeEmpty();
            plan.Problems.Should().OnlyContain(problem =>
                problem.Code == InstanceMigrationProblemCode.SubProcessNotSupported);
        }
    }

    // Testzweck: Prüft, dass alle Hindernisse gemeldet werden und nicht nur das erste.
    [Test]
    public async Task ReportsAllProblems()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewOtherProcessRenamed_v2.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        plan.Problems.Select(problem => problem.Code).Should().Equal(
            InstanceMigrationProblemCode.ProcessChanged,
            InstanceMigrationProblemCode.FlowNodeMissing);
    }

    // Testzweck: Prüft, dass ein abgelehnter Plan nicht versehentlich angewendet werden kann.
    [Test]
    public async Task ApplyRejectsNonMigratablePlan()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewRenamed_v2.bpmn");

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);

        var apply = () => InstanceMigration.Apply(plan, Helper.TestFlowzerConfig);
        apply.Should().Throw<InvalidOperationException>();
    }

    // Testzweck: Prüft, dass ein im Zielmodell neu angehängtes Boundary-Event am umgezogenen
    // Token aktiv wird, damit der Timer nach dem Umzug wirklich greift.
    [Test]
    public async Task MigratedTokenPicksUpNewBoundaryEvent()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewBoundary_v2.bpmn");
        var waitingToken = instance.GetActiveUserTasks().Single();
        waitingToken.ActiveBoundaryEvents.Should().BeEmpty();

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);
        plan.IsMigratable.Should().BeTrue();

        var migratedTokens = InstanceMigration.Apply(plan, Helper.TestFlowzerConfig);

        var migratedWaitingToken = migratedTokens.Single(token => token.Id == waitingToken.Id);
        using (new AssertionScope())
        {
            migratedWaitingToken.ActiveBoundaryEvents.Should().ContainSingle()
                .Which.Should().BeOfType<FlowzerBoundaryTimerEvent>()
                .Which.Id.Should().Be("BoundaryTimer_Review");
            waitingToken.ActiveBoundaryEvents.Should().BeEmpty();
        }
    }

    // Testzweck: Hat ein nicht unterbrechendes Boundary-Event am wartenden Knoten bereits
    // ausgelöst, würde der Umzug es erneut scharf schalten und es liefe ein zweites Mal.
    // Solche Instanzen bleiben deshalb unverändert.
    [Test]
    public async Task ReportsBoundaryEventThatAlreadyTriggered()
    {
        var boundaryProcess = await LoadProcess("MigrationReviewBoundary_v2.bpmn");
        var startData = (ExpandoObject)new { Antragsteller = "Lukas" }.ToDynamic()!;
        var instance = Helper.CreateProcessEngine(boundaryProcess).StartProcess(startData);
        var waitingToken = instance.GetActiveUserTasks().Single();
        waitingToken.ActiveBoundaryEvents.Should().ContainSingle();
        // So hinterlässt die Engine den Token, nachdem der Timer ausgelöst hat.
        waitingToken.ActiveBoundaryEvents.Clear();

        var plan = InstanceMigration.Plan(instance.Tokens, await LoadProcess("MigrationReviewBoundary_v2.bpmn"));

        plan.IsMigratable.Should().BeFalse();
        plan.Problems.Should().ContainSingle()
            .Which.Should().Match<InstanceMigrationProblem>(problem =>
                problem.Code == InstanceMigrationProblemCode.BoundaryEventAlreadyTriggered
                && problem.FlowNodeId == "UserTask_Review");
    }

    // Testzweck: Prüft den Kern der Handzuordnung: Ein wartender Knoten, den es in der
    // Zielversion nicht mehr gibt, wird auf seinen umbenannten Nachfolger geführt, und die
    // Instanz läuft danach wirklich auf dem Pfad der Zielversion weiter.
    [Test]
    public async Task MigratesMappedFlowNodeOntoRenamedTarget()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewMapping_v2.bpmn");

        var waitingToken = instance.GetActiveUserTasks().Single();
        var sourceTokens = instance.Tokens.ToArray();
        var startTime = waitingToken.StartTime;
        var lastStateChangeTime = waitingToken.LastStateChangeTime;

        var plan = InstanceMigration.Plan(
            sourceTokens,
            targetProcess,
            new Dictionary<string, string> { ["UserTask_Review"] = "UserTask_ReviewNew" });

        using (new AssertionScope())
        {
            plan.Problems.Should().BeEmpty();
            plan.IsMigratable.Should().BeTrue();
            plan.FlowNodeIdsNeedingMapping.Should().BeEmpty();
            plan.TargetFlowNodeOf(waitingToken.Id).Id.Should().Be("UserTask_ReviewNew");
        }

        var migratedTokens = InstanceMigration.Apply(plan, Helper.TestFlowzerConfig);

        var migratedWaitingToken = migratedTokens.Single(token => token.Id == waitingToken.Id);
        using (new AssertionScope())
        {
            migratedTokens.Select(token => token.Id).Should().Equal(sourceTokens.Select(token => token.Id));
            migratedTokens.Single(token => token.ParentTokenId == null)
                .Variables.GetValue<string>("Antragsteller").Should().Be("Lukas");
            migratedWaitingToken.State.Should().Be(FlowNodeState.Active);
            migratedWaitingToken.StartTime.Should().Be(startTime);
            migratedWaitingToken.LastStateChangeTime.Should().Be(lastStateChangeTime);
            migratedWaitingToken.Variables.Should().BeSameAs(waitingToken.Variables);
            migratedWaitingToken.CurrentFlowNode!.Id.Should().Be("UserTask_ReviewNew");
            migratedWaitingToken.CurrentFlowNode.Should().BeOfType<UserTask>()
                .Which.Implementation.Should().Be("review-form-v2");

            // Die Eingabe bleibt unangetastet, damit ein abgelehnter Umzug nichts hinterlässt.
            waitingToken.CurrentFlowNode!.Id.Should().Be("UserTask_Review");
        }

        var migratedInstance = new InstanceEngine(migratedTokens, Helper.TestFlowzerConfig);
        migratedInstance.HandleTaskResult(waitingToken.Id, new ExpandoObject());

        // Der eigentliche Zweck der Zuordnung: Die Instanz läuft ab dem zugeordneten Knoten auf
        // den Sequenzflüssen der Zielversion weiter.
        migratedInstance.GetActiveUserTasks().Single().CurrentFlowNode!.Id.Should().Be("UserTask_Approve");
    }

    // Testzweck: Prüft, dass der Plan der Oberfläche genau die Knoten nennt, für die sie eine
    // Zuordnung erfragen muss — und sie nach der Zuordnung nicht mehr nennt.
    [Test]
    public async Task ListsFlowNodesNeedingMappingUntilTheyAreMapped()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewMapping_v2.bpmn");

        var withoutMapping = InstanceMigration.Plan(instance.Tokens, targetProcess);
        var withMapping = InstanceMigration.Plan(
            instance.Tokens,
            targetProcess,
            new Dictionary<string, string> { ["UserTask_Review"] = "UserTask_ReviewNew" });

        using (new AssertionScope())
        {
            withoutMapping.FlowNodeIdsNeedingMapping.Should().Equal("UserTask_Review");
            withoutMapping.Problems.Should().ContainSingle()
                .Which.Code.Should().Be(InstanceMigrationProblemCode.FlowNodeMissing);
            withMapping.FlowNodeIdsNeedingMapping.Should().BeEmpty();
        }
    }

    // Testzweck: Prüft, dass eine Zuordnung auf einen Knoten, den die Zielversion nicht kennt,
    // als eigener Befund erscheint — die Oberfläche muss die Zuordnung korrigieren lassen,
    // statt den Knoten erneut als unzugeordnet auszuweisen.
    [Test]
    public async Task ReportsMissingMappingTarget()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewMapping_v2.bpmn");

        var plan = InstanceMigration.Plan(
            instance.Tokens,
            targetProcess,
            new Dictionary<string, string> { ["UserTask_Review"] = "UserTask_Ghost" });

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.MappingTargetMissing);
            problem.FlowNodeId.Should().Be("UserTask_Review");
            problem.Message.Should().Contain("UserTask_Review").And.Contain("UserTask_Ghost");
            plan.FlowNodeIdsNeedingMapping.Should().BeEmpty();
        }
    }

    // Testzweck: Prüft die Produktentscheidung, dass eine Zuordnung den Elementtyp nicht
    // wechseln darf: Eine Aufgabe auf ein Gateway zu schieben ergäbe einen Zustand, den das
    // Zielmodell nicht kennt.
    [Test]
    public async Task ReportsChangedFlowNodeTypeForMappedTarget()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewMapping_v2.bpmn");

        var plan = InstanceMigration.Plan(
            instance.Tokens,
            targetProcess,
            new Dictionary<string, string> { ["UserTask_Review"] = "Gateway_Decide" });

        using (new AssertionScope())
        {
            plan.IsMigratable.Should().BeFalse();
            var problem = plan.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.FlowNodeTypeChanged);
            problem.FlowNodeId.Should().Be("UserTask_Review");
            problem.Message.Should().Contain("Gateway_Decide");
        }
    }

    // Testzweck: Prüft, dass eine Zuordnung, an der nichts wartet, folgenlos bleibt. Die
    // Oberfläche schickt die Zuordnung für alle Instanzen der Anfrage; eine Instanz, die den
    // Knoten schon hinter sich hat, darf daran nicht scheitern.
    [Test]
    public async Task IgnoresMappingEntriesWithoutWaitingToken()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReview_v2.bpmn");

        var plan = InstanceMigration.Plan(
            instance.Tokens,
            targetProcess,
            new Dictionary<string, string>
            {
                ["StartEvent_1"] = "UserTask_Approve",
                ["UserTask_Vanished"] = "UserTask_Ghost"
            });

        using (new AssertionScope())
        {
            plan.Problems.Should().BeEmpty();
            plan.FlowNodeIdsNeedingMapping.Should().BeEmpty();
            plan.TargetFlowNodeOf(instance.GetActiveUserTasks().Single().Id).Id.Should().Be("UserTask_Review");
        }
    }

    // Testzweck: Prüft, dass die Prüfungen des Ziels wirklich am zugeordneten Knoten hängen:
    // Ein zugeordneter Service-Task mit anderem Auftragstyp trüge einen bereits eingereihten
    // Auftrag mit falschem Typ weiter.
    [Test]
    public async Task ReportsChangedServiceTaskTypeForMappedTarget()
    {
        var instance = await Helper.StartFirstProcessOfFile("MigrationService_v1.bpmn");
        var targetProcess = await LoadProcess("MigrationServiceRenamed_v2.bpmn");

        var ontoOtherType = InstanceMigration.Plan(
            instance.Tokens,
            targetProcess,
            new Dictionary<string, string> { ["ServiceTask_Fetch"] = "ServiceTask_Enrich" });
        var ontoSameType = InstanceMigration.Plan(
            instance.Tokens,
            targetProcess,
            new Dictionary<string, string> { ["ServiceTask_Fetch"] = "ServiceTask_FetchRenamed" });

        using (new AssertionScope())
        {
            var problem = ontoOtherType.Problems.Should().ContainSingle().Subject;
            problem.Code.Should().Be(InstanceMigrationProblemCode.ServiceTaskTypeChanged);
            problem.FlowNodeId.Should().Be("ServiceTask_Fetch");
            ontoSameType.Problems.Should().BeEmpty();
            ontoSameType.TargetFlowNodeOf(ontoSameType.WaitingTokens.Single().Id)
                .Id.Should().Be("ServiceTask_FetchRenamed");
        }
    }

    // Testzweck: Prüft, dass der umgezogene Token die Boundary-Events des zugeordneten Knotens
    // trägt — sonst liefe die Instanz nach der Zuordnung ohne ihre Fristen weiter.
    [Test]
    public async Task MigratedTokenPicksUpBoundaryEventOfMappedTarget()
    {
        var instance = await StartReviewInstance();
        var targetProcess = await LoadProcess("MigrationReviewMappingBoundary_v2.bpmn");
        var waitingToken = instance.GetActiveUserTasks().Single();

        var plan = InstanceMigration.Plan(
            instance.Tokens,
            targetProcess,
            new Dictionary<string, string> { ["UserTask_Review"] = "UserTask_ReviewNew" });
        plan.IsMigratable.Should().BeTrue();

        var migratedTokens = InstanceMigration.Apply(plan, Helper.TestFlowzerConfig);

        migratedTokens.Single(token => token.Id == waitingToken.Id).ActiveBoundaryEvents
            .Should().ContainSingle()
            .Which.Should().BeOfType<FlowzerBoundaryTimerEvent>()
            .Which.Id.Should().Be("BoundaryTimer_ReviewNew");
    }

    private static Token WithState(Token token, FlowNodeState state) =>
        new()
        {
            Id = token.Id,
            ProcessInstanceId = token.ProcessInstanceId,
            ParentTokenId = token.ParentTokenId,
            CurrentBaseElement = token.CurrentBaseElement,
            ActiveBoundaryEvents = token.ActiveBoundaryEvents,
            State = state
        };

    private static async Task<InstanceEngine> StartReviewInstance()
    {
        var process = await LoadProcess("MigrationReview_v1.bpmn");
        var startData = (ExpandoObject)new { Antragsteller = "Lukas" }.ToDynamic()!;
        return Helper.CreateProcessEngine(process).StartProcess(startData);
    }

    private static async Task<Process> LoadProcess(string fileName)
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/" + fileName, FileMode.Open));
        return model.GetProcesses().First();
    }
}
