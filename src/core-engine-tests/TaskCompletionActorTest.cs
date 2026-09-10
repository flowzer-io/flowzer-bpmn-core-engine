using System.Dynamic;
using FluentAssertions;
using Model;

namespace core_engine_tests;

/// <summary>Der Kern trennt vertrauenswürdige Abschlussmetadaten von fremden Ergebnisvariablen.</summary>
public class TaskCompletionActorTest
{
    // Testzweck: Der Akteur stammt vom Aufrufer, nicht aus dem Ergebnis. Das Ergänzen des
    // Legacy-Feldes darf außerdem einen womöglich geteilten Eingabe-Scope nicht verändern.
    [Test]
    public void CompleteTask_ShouldOverrideForgedActorWithoutMutatingTheInput()
    {
        var engine = CreateWaitingInstance();
        var token = engine.GetActiveServiceTasks().Single();
        var actualActor = Guid.NewGuid();
        var forgedActor = Guid.NewGuid();
        ExpandoObject input = new();
        var values = (IDictionary<string, object?>)input;
        values["UserId"] = forgedActor;
        values["answer"] = "approved";

        engine.HandleTaskResult(token.Id, input, actualActor);

        token.CompletedByUserId.Should().Be(actualActor);
        var output = (IDictionary<string, object?>)token.OutputData!;
        output["UserId"].Should().Be(actualActor);
        output["answer"].Should().Be("approved");
        values["UserId"].Should().Be(forgedActor);
        token.OutputData.Should().NotBeSameAs(input);
    }

    // Testzweck: Ein interner Abschluss ohne Identität darf sich keine vom Ergebnis
    // behauptete Identität zuschreiben; null bleibt ausdrücklich „kein Akteur bekannt“.
    [Test]
    public void CompleteTask_ShouldNotTrustResultIdentityWhenNoActorWasProvided()
    {
        var engine = CreateWaitingInstance();
        var token = engine.GetActiveServiceTasks().Single();
        ExpandoObject input = new();
        ((IDictionary<string, object?>)input)["UserId"] = Guid.NewGuid();

        engine.HandleTaskResult(token.Id, input);

        token.CompletedByUserId.Should().BeNull();
        ((IDictionary<string, object?>)token.OutputData!)["UserId"].Should().BeNull();
    }

    private static InstanceEngine CreateWaitingInstance()
    {
        var model = ModelParser.ParseModel("""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="ActorTest" targetNamespace="test">
              <bpmn:process id="Process" isExecutable="true">
                <bpmn:startEvent id="Start"><bpmn:outgoing>ToTask</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="ToTask" sourceRef="Start" targetRef="Task" />
                <bpmn:serviceTask id="Task" name="Test">
                  <bpmn:extensionElements><zeebe:taskDefinition type="test" /></bpmn:extensionElements>
                  <bpmn:incoming>ToTask</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
                </bpmn:serviceTask>
                <bpmn:sequenceFlow id="ToEnd" sourceRef="Task" targetRef="End" />
                <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """);
        return Helper.CreateProcessEngine(model.GetProcesses().Single()).StartProcess();
    }
}
