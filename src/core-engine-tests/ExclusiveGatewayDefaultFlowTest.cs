using BPMN.Common;
using BPMN.Gateways;
using FluentAssertions;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

public class ExclusiveGatewayDefaultFlowTest
{
    // Testzweck: Ein exklusives Gateway mit Standardfluss (default-Attribut) muss laufen.
    // ExclusiveGateway implementierte IHasDefault nicht; der Parser hat das gelesene
    // DefaultId deshalb verworfen und die Laufzeit brach mit "There is a SequenceFlow
    // without a Condition and not default for Exclusive Gateway" ab.
    [Test]
    public async Task ExclusiveGatewayUsesDefaultFlowWhenNoConditionMatches()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("ExklusiveGatewayDefaultFlow.bpmn");

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);

        // Der Master-Token hat keinen FlowNode — deshalb defensiv über den Namen prüfen.
        var reachedNodeNames = instanceEngine.Tokens
            .Select(token => token.CurrentFlowNode?.Name)
            .ToArray();

        reachedNodeNames.Should().Contain("ShouldReached");
        reachedNodeNames.Should().NotContain("ShouldNotReached");
    }

    // Testzweck: Trifft die Bedingung eines anderen Flusses zu, darf der Standardfluss nicht
    // zusaetzlich genommen werden. Das ist der Gegenpfad zum vorigen Test und sichert, dass der
    // Fix den Standardfluss nur als Rueckfall behandelt.
    [Test]
    public async Task ExclusiveGatewaySuppressesDefaultFlowWhenConditionMatches()
    {
        await using var stream = File.OpenRead("embeddings/ExklusiveGatewayDefaultFlow.bpmn");
        var model = await ModelParser.ParseModel(stream);
        var process = model.GetProcesses().First();
        dynamic variables = new System.Dynamic.ExpandoObject();
        variables.Condition = 1;

        var instanceEngine = Helper.CreateProcessEngine(process).StartProcess((System.Dynamic.ExpandoObject)variables);

        instanceEngine.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
        var reachedNodeNames = instanceEngine.Tokens.Select(token => token.CurrentFlowNode?.Name).ToArray();
        reachedNodeNames.Should().Contain("ShouldNotReached");
        reachedNodeNames.Should().NotContain("ShouldReached");
    }

    // Testzweck: Der Parser muss das default-Attribut eines exklusiven Gateways auf den
    // benannten Sequenzfluss übertragen.
    [Test]
    public async Task ParserMarksDefaultSequenceFlowOfExclusiveGateway()
    {
        await using var stream = File.OpenRead("embeddings/ExklusiveGatewayDefaultFlow.bpmn");
        var model = await ModelParser.ParseModel(stream);
        var process = model.GetProcesses().First();

        var gateway = process.FlowElements.OfType<ExclusiveGateway>().Single();
        gateway.DefaultId.Should().Be("Flow_Default");

        var sequenceFlows = process.FlowElements.OfType<SequenceFlow>().ToArray();
        sequenceFlows.Single(flow => flow.Id == "Flow_Default").FlowzerIsDefault.Should().BeTrue();
        sequenceFlows.Single(flow => flow.Id == "Flow_Conditional").FlowzerIsDefault.Should().BeFalse();
    }
}
