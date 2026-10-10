using System.Dynamic;
using System.Text;
using BPMN.Flowzer.Events;
using core_engine;
using FluentAssertions;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>Fehlerpfade transportieren nur deklarierte Daten, nicht den unterbrochenen Variablenscope.</summary>
public sealed class ErrorEndInputProjectionTest
{
    // Testzweck: Der Parser und die Runtime verbinden Error-End-Eingänge zu einer
    // expliziten Projektion. Ohne Mapping bleiben historische Fehler weiterhin datenlos.
    [TestCase(false)]
    [TestCase(true)]
    public async Task ErrorEnd_ShouldExportOnlyExplicitInputProjection(bool mapReason)
    {
        var projection = mapReason ? """
            <bpmn:extensionElements><zeebe:ioMapping>
              <zeebe:input source="=reason" target="notice" />
            </zeebe:ioMapping></bpmn:extensionElements>
            """ : "";
        var xml = $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="ErrorProjection" targetNamespace="test">
              <bpmn:error id="Return" errorCode="RETURN" />
              <bpmn:process id="Process" isExecutable="true">
                <bpmn:startEvent id="Start" />
                <bpmn:sequenceFlow id="ToScope" sourceRef="Start" targetRef="Round" />
                <bpmn:subProcess id="Round">
                  <bpmn:extensionElements><zeebe:ioMapping>
                    <zeebe:input source='="local-reason"' target="reason" />
                    <zeebe:input source='="scope-only"' target="privateReview" />
                  </zeebe:ioMapping></bpmn:extensionElements>
                  <bpmn:startEvent id="RoundStart" />
                  <bpmn:sequenceFlow id="ToError" sourceRef="RoundStart" targetRef="ErrorEnd" />
                  <bpmn:endEvent id="ErrorEnd">{{projection}}<bpmn:errorEventDefinition errorRef="Return" /></bpmn:endEvent>
                </bpmn:subProcess>
                <bpmn:boundaryEvent id="Catch" attachedToRef="Round" cancelActivity="true"><bpmn:errorEventDefinition errorRef="Return" /></bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="ToCorrection" sourceRef="Catch" targetRef="Correction" />
                <bpmn:userTask id="Correction" />
              </bpmn:process>
            </bpmn:definitions>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var model = await ModelParser.ParseModel(stream);
        dynamic root = new ExpandoObject(); root.reason = "root-unchanged"; root.privateRoot = "root-only";
        var instance = new ProcessEngine(model.GetProcesses().Single(), Helper.TestFlowzerConfig).StartProcess((ExpandoObject)root);
        var error = instance.Tokens.Single(token => token.CurrentBaseElement.Id == "ErrorEnd");
        (((FlowzerErrorEndEvent)error.CurrentBaseElement).InputMappings?.Count).Should().Be(mapReason ? 1 : (int?)null);
        var stored = (IDictionary<string, object?>)instance.MasterToken.Variables!;
        stored["reason"].Should().Be("root-unchanged");
        stored["privateRoot"].Should().Be("root-only");
        stored.Should().NotContainKey("privateReview");
        if (mapReason) stored["notice"].Should().Be("local-reason");
        else stored.Should().NotContainKey("notice");
        instance.GetActiveUserTasks().Should().ContainSingle().Which.CurrentBaseElement.Id.Should().Be("Correction");
    }
}
