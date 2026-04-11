using FluentAssertions;
using FlowzerDemoConsole;
using Model;

namespace core_engine_tests;

public class DemoScenarioRunnerTest
{
    [Test]
    public async System.Threading.Tasks.Task RunAsync_ShouldExecuteTheDocumentedHappyPath()
    {
        var output = new StringWriter();
        var runner = new DemoScenarioRunner();

        var finalInstance = await runner.RunAsync(output);

        finalInstance.State.Should().Be(ProcessInstanceState.Completed);

        var consoleOutput = output.ToString();
        consoleOutput.Should().Contain("Start-Subscription: Start (StartEvent_1)");
        consoleOutput.Should().Contain("Instanz gestartet:");
        consoleOutput.Should().Contain("Bearbeite UserTask: UserTask_1");
        consoleOutput.Should().Contain("Bearbeite ServiceTask: ServiceTask_1");
        consoleOutput.Should().Contain("Prozess abgeschlossen: Completed");
    }
}
