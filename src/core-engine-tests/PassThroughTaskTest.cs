using FluentAssertions;
using Model;

namespace core_engine_tests;

public class PassThroughTaskTest
{
    // Testzweck: Generische/manuelle Arbeit durchläuft auch beim normalen Start den
    // Sequenzfluss bis zum Ende, ohne eine Benutzer- oder Worker-Aufgabe zu erzeugen.
    [TestCase("task")]
    [TestCase("manualTask")]
    public void PlainStart_ShouldPassThroughTask(string taskType)
    {
        var xml = $$"""
            <definitions id="Definitions_1" xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <process id="Process_1" isExecutable="true">
                <startEvent id="Start_1" />
                <{{taskType}} id="Task_1" />
                <endEvent id="End_1" />
                <sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
                <sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />
              </process>
            </definitions>
            """;
        var process = ModelParser.ParseModel(xml).GetProcesses().Single();
        var instance = new ProcessEngine(process).StartProcess();

        instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
        instance.GetActiveUserTasks().Should().BeEmpty();
        instance.GetActiveServiceTasks().Should().BeEmpty();
        instance.Tokens.Should().Contain(token => token.CurrentBaseElement.Id == "Task_1"
            && token.State == FlowNodeState.Completed);
    }
}
