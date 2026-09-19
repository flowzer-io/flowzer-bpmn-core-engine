namespace WebApiEngine.Tests;

/// <summary>
/// Die DMN- und BPMN-Dokumente der Entscheidungstests. Sie stehen hier gemeinsam, damit
/// Ablage-, API- und Laufzeittests dieselbe Entscheidung benutzen und ein Unterschied im
/// Ergebnis wirklich am Code liegt und nicht an zwei verschiedenen Tabellen.
/// </summary>
internal static class DecisionFixtures
{
    internal const string RabattDefinitionId = "rabatt";
    internal const string RabattDecisionId = "rabattstufe";

    /// <summary>
    /// Ueberspringt einen Test, der wirklich rechnet, wenn diese Umgebung kein FEEL hat.
    /// </summary>
    /// <remarks>
    /// Eine Entscheidungstabelle braucht echtes FEEL und damit ClearScript/V8. Faellt das aus
    /// — exotische Plattform, fehlende native Bibliothek —, ist der Test nicht pruefbar und
    /// wird uebersprungen statt rot; die Meldung nennt den Grund. Die Tests, die vor der
    /// Auswertung stehenbleiben (Entscheidung nicht gefunden, mehrdeutig), brauchen das nicht.
    /// </remarks>
    internal static void RequireFeelEngine()
    {
        try
        {
            core_engine.FlowzerConfig.Default.FeelEngine.Evaluate("1 + 1", new Dictionary<string, object?>());
        }
        catch (Exception exception)
        {
            Assert.Ignore("Ohne FEEL-faehigen Ausdrucks-Handler nicht pruefbar: " + exception.Message);
        }
    }

    /// <summary>
    /// Eine Entscheidung mit einer Eingabespalte und einer Ausgabespalte: Ab 100000 Umsatz
    /// gibt es "gold", darunter "standard".
    /// </summary>
    internal static string RabattDmn(string definitionsId = RabattDefinitionId, string name = "Rabattstufen") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/"
                     id="{definitionsId}" name="{name}" namespace="http://flowzer.maass.it/dmn/test">
          <decision id="{RabattDecisionId}" name="Rabattstufe">
            <variable name="rabatt" typeRef="string"/>
            <decisionTable id="{definitionsId}Table" hitPolicy="UNIQUE">
              <input id="{definitionsId}Umsatz">
                <inputExpression id="{definitionsId}UmsatzExpression" typeRef="number">
                  <text>jahresumsatz</text>
                </inputExpression>
              </input>
              <output id="{definitionsId}Output" name="stufe" typeRef="string"/>
              <rule id="{definitionsId}Gross">
                <inputEntry id="{definitionsId}GrossIn"><text>&gt;= 100000</text></inputEntry>
                <outputEntry id="{definitionsId}GrossOut"><text>"gold"</text></outputEntry>
              </rule>
              <rule id="{definitionsId}Klein">
                <inputEntry id="{definitionsId}KleinIn"><text>&lt; 100000</text></inputEntry>
                <outputEntry id="{definitionsId}KleinOut"><text>"standard"</text></outputEntry>
              </rule>
            </decisionTable>
          </decision>
        </definitions>
        """;

    /// <summary>
    /// Eine UNIQUE-Tabelle mit ueberlappenden Regeln: Ab 100000 treffen beide Regeln, und
    /// UNIQUE laesst genau das nicht zu.
    /// </summary>
    internal static string KonfliktDmn() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/"
                     id="konflikt" name="Widerspruch" namespace="http://flowzer.maass.it/dmn/test">
          <decision id="konfliktstufe" name="Widersprechende Stufe">
            <variable name="konflikt" typeRef="string"/>
            <decisionTable id="konfliktTable" hitPolicy="UNIQUE">
              <input id="konfliktUmsatz">
                <inputExpression id="konfliktUmsatzExpression" typeRef="number">
                  <text>jahresumsatz</text>
                </inputExpression>
              </input>
              <output id="konfliktOutput" name="stufe" typeRef="string"/>
              <rule id="konfliktGross">
                <inputEntry id="konfliktGrossIn"><text>&gt;= 100000</text></inputEntry>
                <outputEntry id="konfliktGrossOut"><text>"gold"</text></outputEntry>
              </rule>
              <rule id="konfliktAlle">
                <inputEntry id="konfliktAlleIn"><text>&gt;= 0</text></inputEntry>
                <outputEntry id="konfliktAlleOut"><text>"standard"</text></outputEntry>
              </rule>
            </decisionTable>
          </decision>
        </definitions>
        """;

    /// <summary>Kein gueltiges XML — der DMN-Kern muss das beim Speichern melden.</summary>
    internal const string KaputtesDmn = "<definitions><decision id=\"offen\">";

    /// <summary>
    /// Ein Workflow, der die Entscheidung rechnet und hinter einem Exclusive Gateway
    /// unterschiedlich weiterlaeuft. Genau das ist der Beweis: Das Ergebnis steht im
    /// Prozesskontext und ist in einer Bedingung erreichbar.
    /// </summary>
    internal static string RabattWorkflow(string decisionId = RabattDecisionId) => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
            xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            id="Definitions_Rabatt" targetNamespace="test">
          <bpmn:process id="Process_Rabatt" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:businessRuleTask id="Entscheiden">
              <bpmn:extensionElements>
                <zeebe:calledDecision decisionId="{{decisionId}}" resultVariable="ergebnis" />
              </bpmn:extensionElements>
            </bpmn:businessRuleTask>
            <bpmn:exclusiveGateway id="Weiche" default="Flow_Standard" />
            <bpmn:serviceTask id="GoldBehandeln"><bpmn:extensionElements>
              <zeebe:taskDefinition type="gold" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:serviceTask id="StandardBehandeln"><bpmn:extensionElements>
              <zeebe:taskDefinition type="standard" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="EndeGold" />
            <bpmn:endEvent id="EndeStandard" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Entscheiden" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Entscheiden" targetRef="Weiche" />
            <bpmn:sequenceFlow id="Flow_Gold" sourceRef="Weiche" targetRef="GoldBehandeln">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=ergebnis.stufe = "gold"</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="Flow_Standard" sourceRef="Weiche" targetRef="StandardBehandeln" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="GoldBehandeln" targetRef="EndeGold" />
            <bpmn:sequenceFlow id="Flow_4" sourceRef="StandardBehandeln" targetRef="EndeStandard" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// Ein Workflow mit Error-Boundary am Business-Rule-Task. Er belegt, dass die Fehler der
    /// Entscheidung am Knoten selbst fangbar sind und die Instanz weiterlaeuft.
    /// </summary>
    internal static string WorkflowMitBoundary(string decisionId, string errorCode) => $$"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="Definitions_Boundary" targetNamespace="test">
          <bpmn:error id="Error_Entscheidung" name="Entscheidung gescheitert" errorCode="{{errorCode}}" />
          <bpmn:process id="Process_Boundary" isExecutable="true">
            <bpmn:startEvent id="Start" />
            <bpmn:businessRuleTask id="Entscheiden">
              <bpmn:extensionElements>
                <zeebe:calledDecision decisionId="{{decisionId}}" resultVariable="ergebnis" />
              </bpmn:extensionElements>
            </bpmn:businessRuleTask>
            <bpmn:boundaryEvent id="BoundaryFehler" attachedToRef="Entscheiden">
              <bpmn:errorEventDefinition id="Definition_Fehler" errorRef="Error_Entscheidung" />
            </bpmn:boundaryEvent>
            <bpmn:serviceTask id="Nacharbeit"><bpmn:extensionElements>
              <zeebe:taskDefinition type="nacharbeit" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:serviceTask id="NachDerEntscheidung"><bpmn:extensionElements>
              <zeebe:taskDefinition type="weiter" />
            </bpmn:extensionElements></bpmn:serviceTask>
            <bpmn:endEvent id="Ende" />
            <bpmn:endEvent id="EndeNacharbeit" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start" targetRef="Entscheiden" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Entscheiden" targetRef="NachDerEntscheidung" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="NachDerEntscheidung" targetRef="Ende" />
            <bpmn:sequenceFlow id="Flow_4" sourceRef="BoundaryFehler" targetRef="Nacharbeit" />
            <bpmn:sequenceFlow id="Flow_5" sourceRef="Nacharbeit" targetRef="EndeNacharbeit" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
