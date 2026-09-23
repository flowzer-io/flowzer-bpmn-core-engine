using BPMN.Events;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Ein Event-Subprozess ist der Ereignisfaenger seines Scopes: Solange der Prozess oder der
/// Subprozess laeuft, in dem er steht, ist sein Startereignis scharf.
/// </summary>
public class EventSubProcessTest
{
    // Testzweck: Der Event-Subprozess laeuft nicht von selbst mit; seine Nachricht ist aber von
    // Anfang an als Subscription sichtbar.
    [Test]
    public async Task EventSubProcess_ShouldArmItsStartEventWithoutRunning()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventSubProcessInterruptingMessage.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Main"]);
            instanceEngine.ActiveCatchMessages.Select(message => message.Name)
                .Should().BeEquivalentTo(["Abbruch"]);
            instanceEngine.Tokens.Should().NotContain(token => token.CurrentBaseElement.Id == "EventSub");
        }
    }

    // Testzweck: Ein unterbrechendes Startereignis zieht alles zurueck, was im Scope noch laeuft,
    // und der Event-Subprozess uebernimmt; danach endet die Instanz regulaer.
    [Test]
    public async Task InterruptingMessageStart_ShouldWithdrawTheScopeAndTakeOver()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventSubProcessInterruptingMessage.bpmn");

        instanceEngine.HandleMessage(new Message { Name = "Abbruch" });

        using (new AssertionScope())
        {
            TokenState(instanceEngine, "ServiceTask_Main").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Handle"]);
            // Ein unterbrechender Start ist danach nicht mehr scharf.
            instanceEngine.ActiveCatchMessages.Should().BeEmpty();
        }

        instanceEngine.HandleTaskResult(instanceEngine.GetActiveServiceTasks().Single().Id, null);

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Ein nicht unterbrechendes Startereignis laesst den Scope weiterlaufen; beide
    // Pfade sind danach gleichzeitig aktiv und beide muessen enden.
    [Test]
    public async Task NonInterruptingSignalStart_ShouldRunBesideTheScope()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventSubProcessNonInterruptingSignal.bpmn");

        instanceEngine.HandleSignal("Info");

        using (new AssertionScope())
        {
            TokenState(instanceEngine, "ServiceTask_Main").Should().Be(FlowNodeState.Active);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Main", "ServiceTask_Note"]);
            // Nicht unterbrechend heisst: Das Signal bleibt scharf.
            instanceEngine.ActiveCatchSignals.Should().BeEquivalentTo(["Info"]);
        }

        CompleteServiceTask(instanceEngine, "ServiceTask_Note");
        CompleteServiceTask(instanceEngine, "ServiceTask_Main");

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Der Timer eines Event-Subprozesses laeuft ab dem Beginn seines Scopes und
    // erscheint als Subscription; wird er faellig, unterbricht er den Scope.
    [Test]
    public async Task TimerStart_ShouldBeRelativeToTheScopeStartAndInterruptWhenDue()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventSubProcessTimerStart.bpmn");
        var scopeStart = instanceEngine.MasterToken.LastStateChangeTime;

        var subscription = ((ICatchHandler)instanceEngine).ActiveTimerSubscriptions.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            subscription.FlowNodeId.Should().Be("EventSub_Start");
            subscription.DueAt.Should().BeCloseTo(scopeStart.AddMinutes(10), TimeSpan.FromSeconds(5));
        }

        instanceEngine.HandleTime(scopeStart.AddMinutes(11));

        using (new AssertionScope())
        {
            TokenState(instanceEngine, "ServiceTask_Main").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Escalate"]);
            ((ICatchHandler)instanceEngine).ActiveTimerSubscriptions.Should().BeEmpty();
        }
    }

    // Testzweck: Ein Error-Start-Event faengt einen Fehler des Prozesses, bevor die Instanz
    // daran scheitert.
    [Test]
    public async Task ErrorStart_ShouldCatchAProcessErrorBeforeTheInstanceFails()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventSubProcessErrorStart.bpmn");
        var mainToken = instanceEngine.GetActiveServiceTasks().Single();

        instanceEngine.ThrowBpmnError(mainToken.Id, "PRUEFUNG", "Unterlagen fehlen");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.FailureReason.Should().BeNull();
            TokenState(instanceEngine, "ServiceTask_Main").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Repair"]);
        }
    }

    // Testzweck: Ein Event-Subprozess in einem eingebetteten Subprozess faengt nur dessen
    // Ereignisse und beendet danach genau diesen Scope.
    [Test]
    public async Task EventSubProcessInsideASubProcess_ShouldInterruptOnlyThatScope()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventSubProcessInSubProcess.bpmn");

        instanceEngine.ActiveCatchMessages.Select(message => message.Name)
            .Should().BeEquivalentTo(["InnerAbbruch"]);

        instanceEngine.HandleMessage(new Message { Name = "InnerAbbruch" });

        using (new AssertionScope())
        {
            TokenState(instanceEngine, "ServiceTask_Inner").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_InnerHandle"]);
        }

        CompleteServiceTask(instanceEngine, "ServiceTask_InnerHandle");

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Der Parser liest triggeredByEvent und isInterrupting am Startereignis.
    [Test]
    public async Task ModelParser_ShouldReadTriggeredByEventAndIsInterrupting()
    {
        var interrupting = await ModelParser.ParseModel(
            File.Open("embeddings/EventSubProcessInterruptingMessage.bpmn", FileMode.Open));
        var nonInterrupting = await ModelParser.ParseModel(
            File.Open("embeddings/EventSubProcessNonInterruptingSignal.bpmn", FileMode.Open));

        using (new AssertionScope())
        {
            var interruptingSubProcess = interrupting.GetProcesses().Single()
                .FlowElements.OfType<SubProcess>().Single();
            interruptingSubProcess.TriggeredByEvent.Should().BeTrue();
            interruptingSubProcess.FlowElements.OfType<StartEvent>().Single()
                .FlowzerIsInterrupting.Should().BeTrue();

            nonInterrupting.GetProcesses().Single().FlowElements.OfType<SubProcess>().Single()
                .FlowElements.OfType<StartEvent>().Single()
                .FlowzerIsInterrupting.Should().BeFalse();
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
