using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Ein ereignisbasiertes Gateway stellt mehrere Ereignisse gleichzeitig scharf. Genau eines
/// gewinnt; die uebrigen verschwinden samt ihrer Subscriptions.
/// </summary>
public class EventBasedGatewayTest
{
    // Testzweck: Hinter dem Gateway warten Nachricht und Timer gleichzeitig; beide Subscriptions
    // sind sichtbar, und noch laeuft keiner der beiden Folgepfade.
    [Test]
    public async Task EventBasedGateway_ShouldArmEveryFollowingCatchEventAtOnce()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventBasedGateway.bpmn");

        using (new AssertionScope())
        {
            instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instanceEngine.ActiveCatchMessages.Select(message => message.Name)
                .Should().BeEquivalentTo(["Freigabe"]);
            ((ICatchHandler)instanceEngine).ActiveTimerSubscriptions
                .Select(subscription => subscription.FlowNodeId).Should().BeEquivalentTo(["Catch_Frist"]);
            instanceEngine.GetActiveServiceTasks().Should().BeEmpty();
        }
    }

    // Testzweck: Trifft die Nachricht ein, laeuft ihr Folgefluss weiter und der wartende Timer
    // wird zurueckgezogen — seine Subscription verschwindet damit.
    [Test]
    public async Task ArrivingMessage_ShouldWithdrawTheWaitingTimerBranch()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventBasedGateway.bpmn");

        instanceEngine.HandleMessage(new Message { Name = "Freigabe" });

        using (new AssertionScope())
        {
            TokenState(instanceEngine, "Catch_Freigabe").Should().Be(FlowNodeState.Completed);
            TokenState(instanceEngine, "Catch_Frist").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Approved"]);
            ((ICatchHandler)instanceEngine).ActiveTimerSubscriptions.Should().BeEmpty();
            instanceEngine.ActiveCatchMessages.Should().BeEmpty();
        }
    }

    // Testzweck: Feuert stattdessen der Timer, gilt dasselbe umgekehrt — die Nachrichten-
    // Subscription verschwindet, und nur der Fristpfad laeuft.
    [Test]
    public async Task DueTimer_ShouldWithdrawTheWaitingMessageBranch()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventBasedGateway.bpmn");

        instanceEngine.HandleTime(DateTime.UtcNow.AddHours(2));

        using (new AssertionScope())
        {
            TokenState(instanceEngine, "Catch_Frist").Should().Be(FlowNodeState.Completed);
            TokenState(instanceEngine, "Catch_Freigabe").Should().Be(FlowNodeState.Withdrawn);
            instanceEngine.GetActiveServiceTasks().Select(token => token.CurrentBaseElement.Id)
                .Should().BeEquivalentTo(["ServiceTask_Expired"]);
            instanceEngine.ActiveCatchMessages.Should().BeEmpty();
        }
    }

    // Testzweck: Nach dem gewonnenen Zweig laeuft die Instanz ganz normal zu Ende.
    [Test]
    public async Task WinningBranch_ShouldCompleteTheInstance()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("EventBasedGateway.bpmn");
        instanceEngine.HandleMessage(new Message { Name = "Freigabe" });

        instanceEngine.HandleTaskResult(instanceEngine.GetActiveServiceTasks().Single().Id, null);

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Der Parser liest das ereignisbasierte Gateway als eigenen Gatewaytyp.
    [Test]
    public async Task ModelParser_ShouldReadTheEventBasedGateway()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/EventBasedGateway.bpmn", FileMode.Open));

        model.GetProcesses().Single().FlowElements.OfType<BPMN.Gateways.EventBasedGateway>()
            .Should().ContainSingle().Which.Id.Should().Be("Gateway_Event");
    }

    private static FlowNodeState TokenState(InstanceEngine instanceEngine, string flowNodeId) =>
        instanceEngine.Tokens.Single(token => token.CurrentBaseElement.Id == flowNodeId).State;
}
