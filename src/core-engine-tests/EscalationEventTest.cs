using BPMN.Events;
using BPMN.Flowzer.Events;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Eskalation meldet nach aussen, ohne abzubrechen: Der werfende Pfad laeuft weiter, und eine
/// Eskalation ohne Faenger laesst die Instanz ausdruecklich nicht scheitern.
/// </summary>
public class EscalationEventTest
{
    // Testzweck: Ein nicht unterbrechendes Eskalations-Boundary oeffnet den Eskalationspfad
    // zusaetzlich; der Subprozess laeuft daneben weiter.
    [Test]
    public async Task NonInterruptingBoundary_ShouldRunBesideTheSubProcess()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EscalationBoundaryNonInterrupting.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Inner", "ServiceTask_Notify"]);
            instanceEngine.UnhandledEscalations.Should().BeEmpty();
        }

        CompleteServiceTask(instanceEngine, "ServiceTask_Notify");
        CompleteServiceTask(instanceEngine, "ServiceTask_Inner");

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Ein unterbrechendes Eskalations-Boundary zieht den Subprozess samt allem darin
    // zurueck; nur der Eskalationspfad laeuft weiter.
    [Test]
    public async Task InterruptingBoundary_ShouldWithdrawTheSubProcess()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EscalationBoundaryInterrupting.bpmn");

        using (new AssertionScope())
        {
            TokenState(instanceEngine, "SubProcess_1").Should().Be(FlowNodeState.Withdrawn);
            TokenState(instanceEngine, "Throw_Escalation").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Notify"]);
            instanceEngine.Tokens.Should().NotContain(token => token.CurrentBaseElement.Id == "ServiceTask_Inner");
        }

        CompleteServiceTask(instanceEngine, "ServiceTask_Notify");

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Eine ungefangene Eskalation verfaellt — die Instanz scheitert nicht, der Pfad
    // laeuft weiter, und der Vorgang bleibt als Hinweis nachvollziehbar.
    [Test]
    public async Task UncaughtEscalation_ShouldExpireWithoutFailingTheInstance()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EscalationUncaught.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.FailureReason.Should().BeNull();
            TokenState(instanceEngine, "Throw_Escalation").Should().Be(FlowNodeState.Completed);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_After"]);

            var unhandled = instanceEngine.UnhandledEscalations.Should().ContainSingle().Subject;
            unhandled.EscalationCode.Should().Be("NACHFRAGE");
            unhandled.FlowNodeId.Should().Be("Throw_Escalation");
        }
    }

    // Testzweck: Ein Escalation-End-Event des Subprozesses wird vom Event-Subprozess des
    // Prozesses gefangen; da er nicht unterbricht, laeuft der Hauptpfad daneben weiter.
    [Test]
    public async Task EscalationEnd_ShouldBeCaughtByAnEventSubProcess()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EscalationEventSubProcess.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_After", "ServiceTask_Inform"]);
            instanceEngine.UnhandledEscalations.Should().BeEmpty();
        }

        CompleteServiceTask(instanceEngine, "ServiceTask_Inform");
        CompleteServiceTask(instanceEngine, "ServiceTask_After");

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Die fangbaren Eskalationen einer laufenden Instanz sind ueber die Engine sichtbar.
    [Test]
    public async Task ActiveCatchEscalations_ShouldExposeWhatTheInstanceCanCatch()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EscalationEventSubProcess.bpmn");

        instanceEngine.ActiveCatchEscalations.Select(escalation => escalation.EscalationCode)
            .Should().BeEquivalentTo(["NACHFRAGE"]);
    }

    // Testzweck: Der Parser bindet bpmn:escalation-Wurzelelemente an werfende, fangende und
    // startende Eskalationsereignisse.
    [Test]
    public async Task ModelParser_ShouldBindEscalationRootElementsToTheirEvents()
    {
        var model = await ModelParser.ParseModel(
            File.Open("embeddings/EscalationBoundaryNonInterrupting.bpmn", FileMode.Open));
        var process = model.GetProcesses().Single();
        var subProcess = process.FlowElements.OfType<SubProcess>().Single();

        using (new AssertionScope())
        {
            model.RootElements.OfType<Escalation>().Should().ContainSingle()
                .Which.EscalationCode.Should().Be("NACHFRAGE");
            subProcess.FlowElements.OfType<FlowzerIntermediateEscalationThrowEvent>().Single()
                .Escalation!.EscalationCode.Should().Be("NACHFRAGE");
            var boundary = process.FlowElements.OfType<FlowzerBoundaryEscalationEvent>().Single();
            boundary.Escalation!.EscalationCode.Should().Be("NACHFRAGE");
            boundary.CancelActivity.Should().BeFalse();
        }
    }

    // Testzweck: Der Parser liest ein Escalation-End-Event und ein Escalation-Start-Event.
    [Test]
    public async Task ModelParser_ShouldReadEscalationEndAndStartEvents()
    {
        var model = await ModelParser.ParseModel(
            File.Open("embeddings/EscalationEventSubProcess.bpmn", FileMode.Open));
        var process = model.GetProcesses().Single();

        using (new AssertionScope())
        {
            process.FlowElements.OfType<SubProcess>().Single(subProcess => !subProcess.TriggeredByEvent)
                .FlowElements.OfType<FlowzerEscalationEndEvent>().Single()
                .Escalation!.EscalationCode.Should().Be("NACHFRAGE");
            process.FlowElements.OfType<SubProcess>().Single(subProcess => subProcess.TriggeredByEvent)
                .FlowElements.OfType<FlowzerEscalationStartEvent>().Single()
                .Escalation!.EscalationCode.Should().Be("NACHFRAGE");
        }
    }

    private static void CompleteServiceTask(InstanceEngine instanceEngine, string flowNodeId)
    {
        var token = instanceEngine.GetActiveServiceTasks()
            .Single(candidate => candidate.CurrentBaseElement.Id == flowNodeId);
        instanceEngine.HandleTaskResult(token.Id, null);
    }

    private static FlowNodeState TokenState(InstanceEngine instanceEngine, string flowNodeId) =>
        instanceEngine.Tokens.Single(token => token.CurrentBaseElement.Id == flowNodeId).State;
}
