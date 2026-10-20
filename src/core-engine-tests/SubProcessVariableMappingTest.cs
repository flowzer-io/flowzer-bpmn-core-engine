using System.Dynamic;
using System.Text;
using core_engine;
using FluentAssertions;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>Explizite Subprozess-Eingänge und lokale Sequenzbedingungen ohne Änderung des Instanz-Root-Vertrags.</summary>
public sealed class SubProcessVariableMappingTest
{
    // Testzweck: Ein Subprozess liest seine deklarierten Eingänge aus dem Parent,
    // nicht aus dem gerade erst angelegten leeren eigenen Eingabedatensatz.
    [Test]
    public async Task SubProcessInput_ShouldReadTheParentBeforeCreatingItsOwnVariables()
    {
        var instance = await StartAsync(mappedScope: true);
        var scope = instance.Tokens.Single(token => token.CurrentBaseElement.Id == "Round");
        ((IDictionary<string, object?>)scope.Variables!)["vote"].Should().Be("old");
        ((IDictionary<string, object?>)scope.Variables!)["context"].Should().Be("application");
    }

    // Testzweck: Neue lokale Stimmen bestimmen das Gateway im selben Scope, obwohl
    // im Root ein gleichnamiger älterer Wert steht. GetProcessToken bleibt ausdrücklich Root.
    [Test]
    public async Task MappedSubProcessGateway_ShouldReadLocalVoteNotTheRootValue()
    {
        var instance = await StartAsync(mappedScope: true);
        var task = instance.GetActiveUserTasks().Single();
        dynamic data = new ExpandoObject(); data.answer = "approved";
        instance.HandleTaskResult(task.Id, data);
        instance.Tokens.Should().Contain(token => token.CurrentBaseElement.Id == "LocalApproved" && token.State == FlowNodeState.Active);
        ((IDictionary<string, object?>)instance.MasterToken.Variables!)["vote"].Should().Be("old");
        instance.GetProcessToken(task).Should().BeSameAs(instance.MasterToken);
    }

    // Testzweck: Historische Subprozesse ohne eigenen Variablenscope behalten ihre
    // bestehenden Root-Bedingungen; der engere lokale Lesepfad erzeugt kein neues Leer-Scope.
    [Test]
    public async Task UnmappedSubProcessGateway_ShouldRetainTheHistoricalRootFallback()
    {
        var instance = await StartAsync(mappedScope: false);
        var task = instance.GetActiveUserTasks().Single();
        dynamic data = new ExpandoObject(); data.answer = "approved";
        instance.HandleTaskResult(task.Id, data);
        instance.Tokens.Should().Contain(token => token.CurrentBaseElement.Id == "RootFallback" && token.State == FlowNodeState.Active);
    }

    private static async Task<InstanceEngine> StartAsync(bool mappedScope)
    {
        var inputs = mappedScope ? """
          <bpmn:extensionElements><zeebe:ioMapping>
            <zeebe:input source="=vote" target="vote" />
            <zeebe:input source="=context" target="context" />
          </zeebe:ioMapping></bpmn:extensionElements>
          """ : "";
        var xml = $$"""
          <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="MappingScope" targetNamespace="test">
            <bpmn:process id="MappingProcess" isExecutable="true">
              <bpmn:startEvent id="Start" />
              <bpmn:subProcess id="Round">
                {{inputs}}
                <bpmn:startEvent id="RoundStart" />
                <bpmn:userTask id="Review">
                  <bpmn:extensionElements><zeebe:ioMapping>
                    <zeebe:output source="=answer" target="vote" />
                  </zeebe:ioMapping></bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:exclusiveGateway id="Decision" default="Fallback" />
                <bpmn:userTask id="LocalApproved" /><bpmn:userTask id="RootFallback" />
                <bpmn:sequenceFlow id="S1" sourceRef="RoundStart" targetRef="Review" />
                <bpmn:sequenceFlow id="S2" sourceRef="Review" targetRef="Decision" />
                <bpmn:sequenceFlow id="Approved" sourceRef="Decision" targetRef="LocalApproved">
                  <bpmn:conditionExpression>=vote = "approved"</bpmn:conditionExpression>
                </bpmn:sequenceFlow>
                <bpmn:sequenceFlow id="Fallback" sourceRef="Decision" targetRef="RootFallback" />
              </bpmn:subProcess>
              <bpmn:sequenceFlow id="S0" sourceRef="Start" targetRef="Round" />
            </bpmn:process>
          </bpmn:definitions>
          """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var model = await ModelParser.ParseModel(stream);
        dynamic variables = new ExpandoObject(); variables.vote = "old"; variables.context = "application";
        return new ProcessEngine(model.GetProcesses().Single(), Helper.TestFlowzerConfig).StartProcess(variables);
    }
}
