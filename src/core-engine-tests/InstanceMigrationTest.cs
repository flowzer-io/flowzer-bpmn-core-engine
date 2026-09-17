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
