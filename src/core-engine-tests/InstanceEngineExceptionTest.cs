using FluentAssertions;
using core_engine.Exceptions;
using Variables = System.Dynamic.ExpandoObject;

namespace core_engine_tests;

public class InstanceEngineExceptionTest
{
    // Testzweck: Prüft, dass abgeschlossene Tokens nicht erneut über HandleTaskResult abgeschlossen werden können.
    [Test]
    public async System.Threading.Tasks.Task HandleTaskResult_ShouldThrowFlowzerRuntimeException_WhenTokenIsNotActive()
    {
        var instanceEngine = await Helper.StartFirstProcessOfFile("StartStopWithVariables.bpmn");
        var activeServiceTask = instanceEngine.GetActiveServiceTasks().Single();

        instanceEngine.HandleTaskResult(activeServiceTask.Id, new Variables());

        var action = () => instanceEngine.HandleTaskResult(activeServiceTask.Id, new Variables());

        action.Should()
            .Throw<FlowzerRuntimeException>()
            .WithMessage("Token ist nicht aktiv");
    }
}
