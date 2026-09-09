using core_engine.Exceptions;
using FluentAssertions;

namespace core_engine_tests;

public class BpmnCapabilityMatrixTest
{
    // Testzweck: Der eingebettete Vertrag benennt eine Version und trennt bewusst parsebare von ausführbaren Elementen.
    [Test]
    public void Contract_ShouldExposeVersionedExecutionCapabilities()
    {
        BpmnCapabilityMatrix.Contract.ContractVersion.Should().Be("3");
        BpmnCapabilityMatrix.Contract.Elements.Should().Contain(capability =>
            capability.ElementType == "scriptTask"
            && capability.Modelable
            && capability.Parsable
            && !capability.Executable);
    }

    // Testzweck: Kaputtes XML soll als stabiler, wertefreier Modellfehler erscheinen
    // und nicht als unbehandelter Serverfehler oder Parserdetail nach außen gelangen.
    [Test]
    public void ValidateForDeployment_ShouldRejectMalformedXmlWithStableCode()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment("<bpmn:definitions>");

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.xml.invalid");
        exception.ElementId.Should().BeNull();
        exception.Message.Should().Be("The BPMN XML document is invalid.");
    }

    // Testzweck: Ein vom Parser lesbarer Script-Task darf nicht als ausführbare neue Version gespeichert werden.
    [Test]
    public void ValidateForDeployment_ShouldRejectParsedButNonExecutableElement_WithStableCodeAndElementId()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("<bpmn:scriptTask id=\"Script_1\" />"));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.element.not_executable");
        exception.ElementId.Should().Be("Script_1");
        exception.ContractVersion.Should().Be("3");
    }

    // Testzweck: Alle im Vertrag als nur parsebar markierten P0/P1-Elemente werden mit ihrem eigenen BPMN-Knoten abgelehnt.
    [TestCase("manualTask", "<bpmn:manualTask id='Manual_1' />", "Manual_1")]
    [TestCase("callActivity", "<bpmn:callActivity id='Call_1' />", "Call_1")]
    [TestCase("complexGateway", "<bpmn:complexGateway id='Complex_1' />", "Complex_1")]
    [TestCase("inclusiveGateway", "<bpmn:inclusiveGateway id='Inclusive_1' />", "Inclusive_1")]
    [TestCase("intermediateThrowEvent", "<bpmn:intermediateThrowEvent id='Throw_1' />", "Throw_1")]
    [TestCase("messageEnd", "<bpmn:endEvent id='MessageEnd_1'><bpmn:messageEventDefinition /></bpmn:endEvent>", "MessageEnd_1")]
    [TestCase("signalEnd", "<bpmn:endEvent id='SignalEnd_1'><bpmn:signalEventDefinition /></bpmn:endEvent>", "SignalEnd_1")]
    public void ValidateForDeployment_ShouldRejectEveryKnownNonExecutableCapability(
        string _capability, string flowElement, string elementId)
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess(flowElement));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.element.not_executable");
        exception.ElementId.Should().Be(elementId);
    }

    // Testzweck: Der generische BPMN-Task bleibt ausführbar, weil die Instanzengine ihn explizit behandelt.
    [Test]
    public void ValidateForDeployment_ShouldAcceptGenericTask()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("<bpmn:task id='Task_1' />"));

        action.Should().NotThrow();
    }

    // Testzweck: Ein nacktes Intermediate-Catch-Event darf nicht mehr still als durchlaufendes Ereignis interpretiert werden.
    [Test]
    public void ValidateForDeployment_ShouldRejectPlainIntermediateCatchEvent()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("<bpmn:intermediateCatchEvent id='Catch_1' />"));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.element.not_executable");
        exception.ElementId.Should().Be("Catch_1");
    }

    // Testzweck: Nicht erlaubte Eventdefinitionen werden positionsbezogen vor dem Parser mit der Event-ID abgelehnt.
    [TestCase("startEvent", "errorEventDefinition")]
    [TestCase("intermediateCatchEvent", "escalationEventDefinition")]
    [TestCase("boundaryEvent", "errorEventDefinition")]
    [TestCase("endEvent", "errorEventDefinition")]
    public void ValidateForDeployment_ShouldRejectUnsupportedEventDefinition(string eventType, string definitionType)
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess(
            $"<bpmn:{eventType} id='Event_1'><bpmn:{definitionType} /></bpmn:{eventType}>"));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.event_definition.unsupported");
        exception.ElementId.Should().Be("Event_1");
        exception.PropertyPath.Should().Be("eventDefinition");
    }

    // Testzweck: Unvollständige Flowzer-Konfigurationen verweisen auf die gezielte Modelleigenschaft statt nur auf den ganzen Knoten.
    [TestCase("<bpmn:serviceTask id='Service_1' />", "bpmn.service_task.implementation_required", "extensionElements.taskDefinition.type")]
    [TestCase("<bpmn:userTask id='User_1' />", "bpmn.user_task.form_required", "extensionElements.formDefinition.formKey")]
    [TestCase("<bpmn:startEvent id='Timer_1'><bpmn:timerEventDefinition /></bpmn:startEvent>", "bpmn.timer.definition_required", "timerEventDefinition")]
    [TestCase("<bpmn:startEvent id='Start_1' /><bpmn:sequenceFlow id='Flow_1' sourceRef='Start_1' targetRef='Missing_1' />", "bpmn.sequence_flow.invalid_reference", "sourceRef/targetRef")]
    public void ValidateForDeployment_ShouldExposePropertyPathForIncompleteConfiguration(
        string flowElements, string code, string propertyPath)
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess(flowElements));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be(code);
        exception.PropertyPath.Should().Be(propertyPath);
    }

    // Testzweck: Die Validierung steigt in Subprozesse ab, damit nicht ausführbare Elemente nicht hinter einem Container verborgen bleiben.
    [Test]
    public void ValidateForDeployment_ShouldRejectNonExecutableNestedElement()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("""
            <bpmn:subProcess id="Sub_1">
              <bpmn:manualTask id='Manual_1' />
            </bpmn:subProcess>
            """));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.element.not_executable");
        exception.ElementId.Should().Be("Manual_1");
    }

    // Testzweck: Ein bestehender ausführbarer Start-Ende-Pfad bleibt mit dem neuen Vertrag kompatibel.
    [Test]
    public void ValidateForDeployment_ShouldAcceptExecutablePlainStartModel()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("""
            <bpmn:startEvent id="Start_1" />
            <bpmn:endEvent id="End_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="End_1" />
            """));

        action.Should().NotThrow();
    }

    // Testzweck: Doppelt vergebene BPMN-IDs werden vor der Ausführung am konkret betroffenen Modellelement markiert.
    [Test]
    public void ValidateForDeployment_ShouldRejectDuplicateNonEmptyElementIds()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("""
            <bpmn:startEvent id="Start_1" />
            <bpmn:task id="Task_1" />
            <bpmn:endEvent id="Task_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
            """));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.element.id_duplicate");
        exception.ElementId.Should().Be("Task_1");
        exception.PropertyPath.Should().Be("id");
    }

    // Testzweck: Ein Knoten ohne Pfad von einem Startereignis darf nicht als stiller, nie ausführbarer Prozessbestandteil gespeichert werden.
    [Test]
    public void ValidateForDeployment_ShouldRejectFlowNodeUnreachableFromStartEvent()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("""
            <bpmn:startEvent id="Start_1" />
            <bpmn:endEvent id="End_1" />
            <bpmn:task id="Task_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="End_1" />
            """));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.flow_node.unreachable");
        exception.ElementId.Should().Be("Task_1");
        exception.PropertyPath.Should().Be("incoming");
    }

    // Testzweck: Ein unterstütztes Boundary-Event wird durch seine angeheftete Aktivität
    // aktiviert und darf nicht fälschlich als per SequenceFlow unerreichbar gelten.
    [Test]
    public void ValidateForDeployment_ShouldAcceptSupportedBoundaryEventWithoutIncomingSequenceFlow()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("""
            <bpmn:startEvent id="Start_1" />
            <bpmn:task id="Task_1" />
            <bpmn:boundaryEvent id="Boundary_1" attachedToRef="Task_1">
              <bpmn:timerEventDefinition><bpmn:timeDuration>PT1H</bpmn:timeDuration></bpmn:timerEventDefinition>
            </bpmn:boundaryEvent>
            <bpmn:endEvent id="End_1" />
            <bpmn:endEvent id="Escalated_End_1" />
            <bpmn:sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />
            <bpmn:sequenceFlow id="Flow_3" sourceRef="Boundary_1" targetRef="Escalated_End_1" />
            """));

        action.Should().NotThrow();
    }

    // Testzweck: Der Default-Pfad eines exklusiven Splits muss zu einem tatsächlich ausgehenden Sequenzfluss gehören.
    [Test]
    public void ValidateForDeployment_ShouldRejectExclusiveGatewayDefaultThatIsNotOutgoing()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("""
            <bpmn:startEvent id="Start_1" />
            <bpmn:exclusiveGateway id="Gateway_1" default="Flow_NotOutgoing" />
            <bpmn:endEvent id="End_1" />
            <bpmn:endEvent id="End_2" />
            <bpmn:sequenceFlow id="Flow_ToGateway" sourceRef="Start_1" targetRef="Gateway_1" />
            <bpmn:sequenceFlow id="Flow_Default" sourceRef="Gateway_1" targetRef="End_1" />
            <bpmn:sequenceFlow id="Flow_Conditional" sourceRef="Gateway_1" targetRef="End_2"><bpmn:conditionExpression>approved</bpmn:conditionExpression></bpmn:sequenceFlow>
            """));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.exclusive_gateway.default.invalid_reference");
        exception.ElementId.Should().Be("Gateway_1");
        exception.PropertyPath.Should().Be("default");
    }

    // Testzweck: Jeder nicht als Default markierte Ausgang eines exklusiven Splits benötigt eine eigene Bedingung.
    [Test]
    public void ValidateForDeployment_ShouldRejectExclusiveGatewaySplitWithoutConditionOnNonDefaultFlow()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("""
            <bpmn:startEvent id="Start_1" />
            <bpmn:exclusiveGateway id="Gateway_1" default="Flow_Default" />
            <bpmn:endEvent id="End_1" />
            <bpmn:endEvent id="End_2" />
            <bpmn:sequenceFlow id="Flow_ToGateway" sourceRef="Start_1" targetRef="Gateway_1" />
            <bpmn:sequenceFlow id="Flow_Default" sourceRef="Gateway_1" targetRef="End_1" />
            <bpmn:sequenceFlow id="Flow_Conditional" sourceRef="Gateway_1" targetRef="End_2" />
            """));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.exclusive_gateway.condition_required");
        exception.ElementId.Should().Be("Flow_Conditional");
        exception.PropertyPath.Should().Be("conditionExpression");
    }

    // Testzweck: Verschachtelte Container erhalten ihre eigene Erreichbarkeitsprüfung und dürfen keine Knoten außerhalb ihres Startpfads verstecken.
    [Test]
    public void ValidateForDeployment_ShouldValidateReachabilityInsideSubProcessSeparately()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess("""
            <bpmn:startEvent id="Parent_Start" />
            <bpmn:subProcess id="Sub_1">
              <bpmn:startEvent id="Sub_Start" />
              <bpmn:endEvent id="Sub_End" />
              <bpmn:task id="Sub_Unreachable" />
              <bpmn:sequenceFlow id="Sub_Flow" sourceRef="Sub_Start" targetRef="Sub_End" />
            </bpmn:subProcess>
            <bpmn:endEvent id="Parent_End" />
            <bpmn:sequenceFlow id="Parent_Flow_1" sourceRef="Parent_Start" targetRef="Sub_1" />
            <bpmn:sequenceFlow id="Parent_Flow_2" sourceRef="Sub_1" targetRef="Parent_End" />
            """));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.flow_node.unreachable");
        exception.ElementId.Should().Be("Sub_Unreachable");
        exception.PropertyPath.Should().Be("incoming");
    }

    // Testzweck: KI-Aufgaben verwenden einen eindeutig versionierten Flowzer-Vertrag statt
    // eines beliebigen Worker-Strings, damit Modellierer und Laufzeit dieselben Felder lesen.
    [Test]
    public void ValidateForAuthoring_ShouldAcceptCompleteAiTaskContract()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForAuthoring(CreateProcess(AiTask()));

        action.Should().NotThrow();
    }

    // Testzweck: Seit der dauerhafte KI-Executor Providerergebnis und Engine-Fortschritt
    // atomar verbindet, ist der vollstaendige KI-Vertrag produktiv deploybar.
    [Test]
    public void ValidateForDeployment_ShouldAcceptAiTaskWithRuntimeContract()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForDeployment(CreateProcess(AiTask()));

        action.Should().NotThrow();
    }

    // Testzweck: Der reservierte KI-Auftragstyp darf nicht ohne den zugehörigen Vertrag
    // gespeichert werden; sonst sähe das Diagramm wie ein KI-Schritt aus, wäre aber unbestimmt.
    [Test]
    public void ValidateForDeployment_ShouldRejectAiWorkerTypeWithoutContract()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForAuthoring(CreateProcess("""
            <bpmn:serviceTask id="Ai_1">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="flowzer.ai.v1" />
              </bpmn:extensionElements>
            </bpmn:serviceTask>
            """));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.ai_task.contract_required");
        exception.ElementId.Should().Be("Ai_1");
        exception.PropertyPath.Should().Be("extensionElements.aiTask");
    }

    // Testzweck: Unbekannte oder geheime Attribute dürfen nicht unbemerkt in BPMN gelangen;
    // insbesondere darf eine Secret-Referenz nie Bestandteil des exportierbaren Modells sein.
    [Test]
    public void ValidateForDeployment_ShouldRejectUnsupportedAiTaskAttribute()
    {
        var action = () => BpmnCapabilityMatrix.ValidateForAuthoring(CreateProcess(
            AiTask(" secretReference=\"env:FLOWZER_AI_KEY\"")));

        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be("bpmn.ai_task.attribute_unsupported");
        exception.ElementId.Should().Be("Ai_1");
        exception.PropertyPath.Should().Be("extensionElements.aiTask.secretReference");
    }

    // Testzweck: Prompt, Ergebnisschema und Grenzen werden serverseitig geprüft und können
    // daher nicht durch einen API-Aufruf ohne Browser umgangen werden.
    [TestCase("contractVersion=\"2\"", "bpmn.ai_task.contract_version_unsupported", "extensionElements.aiTask.contractVersion")]
    [TestCase("instructionVersion=\"0\"", "bpmn.ai_task.instruction_version_invalid", "extensionElements.aiTask.instructionVersion")]
    [TestCase("maxInputTokens=\"0\"", "bpmn.ai_task.limit_invalid", "extensionElements.aiTask.maxInputTokens")]
    [TestCase("maxOutputTokens=\"32769\"", "bpmn.ai_task.limit_invalid", "extensionElements.aiTask.maxOutputTokens")]
    [TestCase("timeoutSeconds=\"301\"", "bpmn.ai_task.limit_invalid", "extensionElements.aiTask.timeoutSeconds")]
    public void ValidateForDeployment_ShouldRejectInvalidAiTaskAttribute(
        string replacement,
        string code,
        string propertyPath)
    {
        var attribute = replacement.Split('=')[0];
        var xml = AiTask().Replace($"{attribute}=\"{DefaultAiAttribute(attribute)}\"", replacement, StringComparison.Ordinal);

        Action action = () => BpmnCapabilityMatrix.ValidateForAuthoring(CreateProcess(xml));
        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be(code);
        exception.PropertyPath.Should().Be(propertyPath);
    }

    // Testzweck: Ein KI-Schritt muss Eingaben und eine Ergebniszuordnung deklarieren; die
    // Laufzeit darf weder den gesamten Prozesskontext senden noch Ausgaben beliebig verteilen.
    [TestCase("<zeebe:input source=\"=request\" target=\"request\" />", "bpmn.ai_task.output_mapping_required", "extensionElements.ioMapping.output")]
    [TestCase("<zeebe:output source=\"=result\" target=\"classification\" />", "bpmn.ai_task.input_mapping_required", "extensionElements.ioMapping.input")]
    public void ValidateForDeployment_ShouldRequireAiTaskDataMappings(
        string onlyMapping,
        string code,
        string propertyPath)
    {
        var xml = AiTask().Replace(
            "<zeebe:input source=\"=request\" target=\"request\" /><zeebe:output source=\"=result\" target=\"classification\" />",
            onlyMapping,
            StringComparison.Ordinal);

        Action action = () => BpmnCapabilityMatrix.ValidateForAuthoring(CreateProcess(xml));
        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be(code);
        exception.PropertyPath.Should().Be(propertyPath);
    }

    // Testzweck: Das Ergebnisschema ist kein beliebiger Text, sondern ein JSON-Objektschema,
    // das spätere Providerantworten deterministisch prüfen kann.
    [TestCase("not-json", "bpmn.ai_task.result_schema_invalid")]
    [TestCase("{&quot;type&quot;:&quot;string&quot;}", "bpmn.ai_task.result_schema_object_required")]
    [TestCase("{&quot;type&quot;:&quot;object&quot;,&quot;$ref&quot;:&quot;https://example.test/schema&quot;}", "bpmn.ai_task.result_schema_unsupported")]
    public void ValidateForDeployment_ShouldRejectInvalidAiTaskResultSchema(string schema, string code)
    {
        var xml = AiTask().Replace(
            "{&quot;type&quot;:&quot;object&quot;,&quot;properties&quot;:{&quot;category&quot;:{&quot;type&quot;:&quot;string&quot;} } }",
            schema,
            StringComparison.Ordinal);

        Action action = () => BpmnCapabilityMatrix.ValidateForAuthoring(CreateProcess(xml));
        var exception = action.Should().Throw<BpmnCapabilityValidationException>().Which;
        exception.Code.Should().Be(code);
        exception.PropertyPath.Should().Be("extensionElements.aiTask.resultSchema");
    }

    private static string DefaultAiAttribute(string name) => name switch
    {
        "contractVersion" => "1",
        "instructionVersion" => "1",
        "maxInputTokens" => "4096",
        "maxOutputTokens" => "1024",
        "timeoutSeconds" => "60",
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static string AiTask(string additionalAttributes = "") => $$$"""
        <bpmn:serviceTask id="Ai_1">
          <bpmn:extensionElements>
            <zeebe:taskDefinition type="flowzer.ai.v1" retries="2" />
            <flowzer:aiTask xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                           contractVersion="1"
                           connectionId="118adeb6-65a4-4e57-a03b-d3b0a3300ac9"
                           model="model-a"
                           instructionVersion="1"
                           maxInputTokens="4096"
                           maxOutputTokens="1024"
                           timeoutSeconds="60"{{{additionalAttributes}}}>
              <flowzer:instruction>Classify the request.</flowzer:instruction>
              <flowzer:resultSchema>{&quot;type&quot;:&quot;object&quot;,&quot;properties&quot;:{&quot;category&quot;:{&quot;type&quot;:&quot;string&quot;} } }</flowzer:resultSchema>
            </flowzer:aiTask>
            <zeebe:ioMapping><zeebe:input source="=request" target="request" /><zeebe:output source="=result" target="classification" /></zeebe:ioMapping>
          </bpmn:extensionElements>
        </bpmn:serviceTask>
        """;

    private static string CreateProcess(string flowElements) => $"""
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                          id="Definitions_1">
          <bpmn:process id="Process_1" isExecutable="true">
            {flowElements}
          </bpmn:process>
        </bpmn:definitions>
        """;
}
