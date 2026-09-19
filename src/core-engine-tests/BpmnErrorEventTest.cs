using BPMN.Events;
using BPMN.Flowzer.Events;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Fachliche Fehler laufen auf BPMN-Ebene weiter: Ein Error-End-Event loest einen Fehler aus,
/// ein Error-Boundary faengt ihn unterbrechend, und ohne Faenger scheitert die Instanz mit
/// nachvollziehbarer Begruendung.
/// </summary>
public class BpmnErrorEventTest
{
    // Testzweck: Ein Error-End-Event im Subprozess wird vom Boundary mit passendem Code gefangen,
    // die uebrigen Tokens des Subprozesses werden zurueckgezogen und der Folgepfad laeuft weiter.
    [Test]
    public async Task ErrorEndInSubProcess_ShouldBeCaughtByMatchingBoundaryAndWithdrawSiblings()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ErrorBoundaryOnSubProcess.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.FailureReason.Should().BeNull();

            TokenState(instanceEngine, "ServiceTask_Wait").Should().Be(FlowNodeState.Withdrawn);
            TokenState(instanceEngine, "SubProcess_1").Should().Be(FlowNodeState.Withdrawn);
            TokenState(instanceEngine, "ErrorEnd_1").Should().Be(FlowNodeState.Completed);
            TokenState(instanceEngine, "BoundaryError_1").Should().Be(FlowNodeState.Completed);

            // Der Folgepfad des Boundary laeuft: Die Instanz wartet jetzt auf die Nacharbeit.
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Recover"]);
            instanceEngine.Tokens.Should().NotContain(token => token.CurrentBaseElement.Id == "EndEvent_Regular");
        }

        var recoverToken = instanceEngine.GetActiveServiceTasks().Single();
        instanceEngine.HandleTaskResult(recoverToken.Id, null);

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Traegt das Boundary einen anderen Fehlercode, faengt es nicht; die Instanz
    // scheitert mit einer Begruendung, die den unbehandelten Code benennt.
    [Test]
    public async Task ErrorEndWithUnmatchedCode_ShouldFailTheInstanceWithAReason()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ErrorBoundaryCodeMismatch.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Failed);
            instanceEngine.FailureReason.Should().Be(
                "Unhandled BPMN error 'ANTRAG_UNVOLLSTAENDIG' at 'ErrorEnd_1'.");
            instanceEngine.Tokens.Should().NotContain(token => token.CurrentBaseElement.Id == "ServiceTask_Recover");
        }
    }

    // Testzweck: Ein Error-Boundary ohne errorRef faengt jeden Fehlercode.
    [Test]
    public async Task ErrorBoundaryWithoutErrorRef_ShouldCatchEveryCode()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ErrorBoundaryCatchAll.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.FailureReason.Should().BeNull();
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Recover"]);
        }
    }

    // Testzweck: Faengt der innere Scope den Fehler nicht, wandert er nach aussen bis zum
    // Boundary des aeusseren Subprozesses; beide Subprozesse werden dabei zurueckgezogen.
    [Test]
    public async Task ErrorInNestedSubProcess_ShouldTravelOutwardsToTheEnclosingBoundary()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ErrorBoundaryNestedSubProcess.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            TokenState(instanceEngine, "SubProcess_Inner").Should().Be(FlowNodeState.Withdrawn);
            TokenState(instanceEngine, "SubProcess_Outer").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Recover"]);
        }
    }

    // Testzweck: Ein Error-End-Event auf Prozessebene hat keinen moeglichen Faenger und
    // beendet die Instanz als gescheitert.
    [Test]
    public async Task ErrorEndOnProcessLevel_ShouldFailTheInstance()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ErrorEndOnProcessLevel.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Failed);
            instanceEngine.FailureReason.Should().Be(
                "Unhandled BPMN error 'ANTRAG_UNVOLLSTAENDIG' at 'ErrorEnd_1'.");
            // Der Knoten bleibt als erreicht sichtbar, damit der Laufzeitverlauf ihn zeigt.
            TokenState(instanceEngine, "ErrorEnd_1").Should().Be(FlowNodeState.Completed);
        }
    }

    // Testzweck: Ein Worker-Fehler an einem Service-Task wird vom Boundary am Task selbst
    // gefangen; die ebenfalls vorhandene Message-Subscription des Tasks verschwindet dabei.
    [Test]
    public async Task ThrownWorkerError_ShouldBeCaughtByTheBoundaryOfTheServiceTask()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ErrorBoundaryOnServiceTask.bpmn");
        var serviceTaskToken = instanceEngine.GetActiveServiceTasks().Single();
        instanceEngine.ActiveCatchMessages.Should().ContainSingle(message => message.Name == "Abbruch");

        instanceEngine.ThrowBpmnError(serviceTaskToken.Id, "BONITAET", "Score zu niedrig");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            TokenState(instanceEngine, "ServiceTask_1").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Recover"]);
            // Andere Boundary-Events desselben Knotens sind mit ihm deaktiviert.
            instanceEngine.ActiveCatchMessages.Should().BeEmpty();
        }
    }

    // Testzweck: Ohne passendes Boundary scheitert die Instanz auch beim Worker-Fehler, und die
    // Begruendung traegt sowohl den Code als auch die Meldung des Workers.
    [Test]
    public async Task ThrownWorkerErrorWithoutBoundary_ShouldFailTheInstance()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ErrorBoundaryOnServiceTask.bpmn");
        var serviceTaskToken = instanceEngine.GetActiveServiceTasks().Single();

        instanceEngine.ThrowBpmnError(serviceTaskToken.Id, "UNBEKANNT", "Score zu niedrig");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Failed);
            instanceEngine.FailureReason.Should().Be(
                "Unhandled BPMN error 'UNBEKANNT' at 'ServiceTask_1'. Score zu niedrig");
        }
    }

    // Testzweck: Der Parser liest bpmn:error-Wurzelelemente samt Code und verbindet sie mit
    // Error-End- und Error-Boundary-Event; ein fehlendes errorRef bleibt zulaessig.
    [Test]
    public async Task ModelParser_ShouldBindErrorRootElementsToTheirEvents()
    {
        var model = await ModelParser.ParseModel(
            File.Open("embeddings/ErrorBoundaryOnSubProcess.bpmn", FileMode.Open));
        var process = model.GetProcesses().Single();
        var subProcess = process.FlowElements.OfType<SubProcess>().Single();

        using (new AssertionScope())
        {
            model.RootElements.OfType<BPMN.Common.Error>().Should().ContainSingle()
                .Which.ErrorCode.Should().Be("ANTRAG_UNVOLLSTAENDIG");
            subProcess.FlowElements.OfType<FlowzerErrorEndEvent>().Single()
                .Error!.ErrorCode.Should().Be("ANTRAG_UNVOLLSTAENDIG");
            var boundary = process.FlowElements.OfType<FlowzerBoundaryErrorEvent>().Single();
            boundary.Error!.ErrorCode.Should().Be("ANTRAG_UNVOLLSTAENDIG");
            boundary.CancelActivity.Should().BeTrue();
            boundary.AttachedToRef.Id.Should().Be("SubProcess_1");
        }
    }

    // Testzweck: Ein Error-Boundary ohne errorRef wird ohne Fehlerbezug geparst.
    [Test]
    public async Task ModelParser_ShouldAcceptAnErrorBoundaryWithoutErrorRef()
    {
        var model = await ModelParser.ParseModel(
            File.Open("embeddings/ErrorBoundaryCatchAll.bpmn", FileMode.Open));

        model.GetProcesses().Single().FlowElements.OfType<FlowzerBoundaryErrorEvent>()
            .Single().Error.Should().BeNull();
    }

    private static FlowNodeState TokenState(InstanceEngine instanceEngine, string flowNodeId) =>
        instanceEngine.Tokens.Single(token => token.CurrentBaseElement.Id == flowNodeId).State;
}
