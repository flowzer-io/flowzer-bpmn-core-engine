using BPMN.Flowzer;
using BPMN.Process;
using FluentAssertions;
using FluentAssertions.Execution;
using Flowzer.Shared;

namespace core_engine_tests;

/// <summary>
/// Das Startformular am Startereignis: die Ermittlung seines Form-Keys und der Instanzstart
/// mit den ausgefüllten Werten.
/// </summary>
public class StartFormTest
{
    // Testzweck: Prüft, dass ein Prozess ohne Startformular keinen Form-Key meldet — ein
    // solcher Workflow startet weiterhin ohne Eingabe.
    [Test]
    public void FormKeyOf_ShouldReturnNothing_WhenNoStartEventCarriesAForm()
    {
        var process = ParseProcess(StartEventsXml("""<bpmn:startEvent id="StartEvent_1" />"""));

        var result = FlowzerStartForm.FormKeyOf(process);

        using (new AssertionScope())
        {
            result.FormKey.Should().BeNull();
            result.ErrorMessage.Should().BeNull();
        }
    }

    // Testzweck: Prüft, dass genau ein Startformular als Form-Key des Prozesses gilt.
    [Test]
    public void FormKeyOf_ShouldReturnTheKey_WhenExactlyOneStartEventCarriesAForm()
    {
        var process = ParseProcess(StartEventsXml("""
                                                    <bpmn:startEvent id="StartEvent_1">
                                                      <bpmn:extensionElements>
                                                        <zeebe:formDefinition formKey="Urlaubsantrag" />
                                                      </bpmn:extensionElements>
                                                    </bpmn:startEvent>
                                                  """));

        var result = FlowzerStartForm.FormKeyOf(process);

        using (new AssertionScope())
        {
            result.FormKey.Should().Be("Urlaubsantrag");
            result.ErrorMessage.Should().BeNull();
        }
    }

    // Testzweck: Prüft, dass mehrere Startformulare in einem Prozess als Fehler gemeldet
    // werden. Welches Formular gälte, entschiede sonst die Reihenfolge im Diagramm.
    [Test]
    public void FormKeyOf_ShouldReportAnError_WhenSeveralStartEventsCarryAForm()
    {
        var process = ParseProcess(StartEventsXml("""
                                                    <bpmn:startEvent id="StartEvent_1">
                                                      <bpmn:extensionElements>
                                                        <zeebe:formDefinition formKey="Urlaubsantrag" />
                                                      </bpmn:extensionElements>
                                                    </bpmn:startEvent>
                                                    <bpmn:startEvent id="StartEvent_2">
                                                      <bpmn:extensionElements>
                                                        <zeebe:formDefinition formKey="Krankmeldung" />
                                                      </bpmn:extensionElements>
                                                    </bpmn:startEvent>
                                                  """));

        var result = FlowzerStartForm.FormKeyOf(process);

        using (new AssertionScope())
        {
            result.FormKey.Should().BeNull();
            result.ErrorMessage.Should().Contain("StartEvent_1").And.Contain("StartEvent_2");
        }
    }

    // Testzweck: Prüft, dass ein Formular an einem Timer-Startereignis nicht als Startformular
    // des Prozesses gilt. Nur der Direktstart fragt Eingaben ab; ein Zeitstart hat niemanden,
    // der sie ausfüllen könnte.
    [Test]
    public void FormKeyOf_ShouldIgnoreStartEventsWaitingForATimer()
    {
        var process = ParseProcess(StartEventsXml("""
                                                    <bpmn:startEvent id="StartEvent_Timer">
                                                      <bpmn:extensionElements>
                                                        <zeebe:formDefinition formKey="Urlaubsantrag" />
                                                      </bpmn:extensionElements>
                                                      <bpmn:timerEventDefinition id="Timer_1">
                                                        <bpmn:timeDuration>PT5S</bpmn:timeDuration>
                                                      </bpmn:timerEventDefinition>
                                                    </bpmn:startEvent>
                                                  """));

        var result = FlowzerStartForm.FormKeyOf(process);

        using (new AssertionScope())
        {
            result.FormKey.Should().BeNull();
            result.ErrorMessage.Should().BeNull();
        }
    }

    // Testzweck: Prüft, dass die Startdaten einer Instanz als Prozessvariablen ankommen —
    // genau das macht ein ausgefülltes Startformular zu den Startwerten des Workflows.
    [Test]
    public void StartProcess_ShouldKeepStartDataAsProcessVariables()
    {
        var process = ParseProcess(StartEventsXml("""<bpmn:startEvent id="StartEvent_1" />"""));
        dynamic startData = new System.Dynamic.ExpandoObject();
        startData.Antragsteller = "Christian";

        var instance = Helper.CreateProcessEngine(process).StartProcess((System.Dynamic.ExpandoObject)startData);

        instance.Tokens.Should()
            .Contain(token => token.OutputData != null && (string?)token.OutputData!.GetValue("Antragsteller") == "Christian");
    }

    // Testzweck: Prüft, dass die Ausgangszuordnungen am Startereignis auf die Startdaten
    // angewendet werden. Erst dadurch lässt sich ein Formularfeld auf einen anderen
    // Variablennamen legen, ohne das Formular umzubauen.
    [Test]
    public void StartProcess_ShouldApplyOutputMappingsOfTheStartEventToTheStartData()
    {
        var process = ParseProcess(StartEventsXml("""
                                                    <bpmn:startEvent id="StartEvent_1">
                                                      <bpmn:extensionElements>
                                                        <zeebe:ioMapping>
                                                          <zeebe:output source="=Antragsteller" target="Mitarbeiter" />
                                                        </zeebe:ioMapping>
                                                      </bpmn:extensionElements>
                                                    </bpmn:startEvent>
                                                  """));
        dynamic startData = new System.Dynamic.ExpandoObject();
        startData.Antragsteller = "Christian";

        var instance = Helper.CreateProcessEngine(process).StartProcess((System.Dynamic.ExpandoObject)startData);

        ((string?)instance.MasterToken.Variables!.GetValue("Mitarbeiter")).Should().Be("Christian");
    }

    // Testzweck: Prueft, dass ein Startereignis im Subprozess kein Startformular des Prozesses
    // ist. Es startet den Subprozess, nicht den Workflow — ein Formular dort fuellte niemand aus.
    [Test]
    public void FormKeyOf_ShouldIgnoreStartEventsInsideASubProcess()
    {
        var process = ParseProcess(StartEventsXml("""
                                                    <bpmn:startEvent id="StartEvent_1" />
                                                    <bpmn:subProcess id="SubProcess_1">
                                                      <bpmn:startEvent id="SubStart_1">
                                                        <bpmn:extensionElements>
                                                          <zeebe:formDefinition formKey="Urlaubsantrag" />
                                                        </bpmn:extensionElements>
                                                      </bpmn:startEvent>
                                                    </bpmn:subProcess>
                                                  """));

        FlowzerStartForm.FormKeyOf(process).FormKey.Should().BeNull();
    }

    private static Process ParseProcess(string xml) => ModelParser.ParseModel(xml).GetProcesses().Single();

    private static string StartEventsXml(string startEvents) =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                           xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                           id="Definitions_StartForm">
           <bpmn:process id="Process_StartForm" isExecutable="true">
         {startEvents}
           </bpmn:process>
         </bpmn:definitions>
         """;
}
