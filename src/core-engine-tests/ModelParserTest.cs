using BPMN.Common;
using BPMN.Events;
using BPMN.Flowzer;
using BPMN.Flowzer.Events;
using BPMN.Gateways;
using BPMN.HumanInteraction;
using BPMN.Process;
using core_engine.Exceptions;
using FluentAssertions;
using FluentAssertions.Execution;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

public class ModelParserTest
{
    // Testzweck: Prüft, dass der Parser die zentrale Beispiel-BPMN mit allen erwarteten Flow-Node-Typen einliest.
    [Test]
    public async Task ReadProcessesTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/AllFlowNodes.bpmn", FileMode.Open));
        Assert.That(model.GetProcesses().Count(), Is.EqualTo(1));

        var process = model.GetProcesses().Single();
        Assert.Multiple(() =>
        {
            Assert.That(process.Id, Is.EqualTo("Process_AllNodes"));
            AssertFlowNodeOfTypes<FlowzerScriptTask>(process, 1, "Activity_04cp0f7");
            AssertFlowNodeOfTypes<ServiceTask>(process, 1);
            AssertFlowNodeOfTypes<StartEvent>(process, 1);
            AssertFlowNodeOfTypes<EndEvent>(process, 3);
            AssertFlowNodeOfTypes<FlowzerBoundaryMessageEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerBoundarySignalEvent>(process, 1);
            AssertFlowNodeOfTypes<IntermediateThrowEvent>(process, 1);
            AssertFlowNodeOfTypes<UserTask>(process, 1);
            AssertFlowNodeOfTypes<ParallelGateway>(process, 2);
            AssertFlowNodeOfTypes<ManualTask>(process, 1);
            AssertFlowNodeOfTypes<ExclusiveGateway>(process, 1);
            AssertFlowNodeOfTypes<FlowzerTerminateEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerMessageStartEvent>(process, 1);
            AssertFlowNodeOfTypes<ReceiveTask>(process, 1);
            AssertFlowNodeOfTypes<FlowzerMessageEndEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerSignalStartEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerSignalEndEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerTimerStartEvent>(process, 2);
            AssertFlowNodeOfTypes<FlowzerIntermediateMessageCatchEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateMessageThrowEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateSignalCatchEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateSignalThrowEvent>(process, 1);
            AssertFlowNodeOfTypes<FlowzerIntermediateTimerCatchEvent>(process, 1);
            
            AssertFlowNodeOfTypes<SequenceFlow>(process, 21);
        });

        var secondModel = await ModelParser.ParseModel(File.Open("embeddings/AllFlowNodes.bpmn", FileMode.Open));
        Assert.That(process.GetHashCode(), Is.EqualTo(secondModel.GetProcesses().Single().GetHashCode()),
            "Der Hashcode des Processes muss bei mehrmaligem Aufruf gleich bleiben");
    }

    // Testzweck: Prüft, dass Subprozesse, verschachtelte Subprozesse und Call Activities korrekt geparst werden.
    [Test]
    public async Task SubprocessParseTest()
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/SubProcess.bpmn", FileMode.Open));
        model.GetProcesses().Should().ContainSingle();
        var process = model.GetProcesses().Single();
        process.FlowElements.OfType<SubProcess>().Select(p => p.Id)
            .Should().HaveCount(3)
            .And.Contain(["Activity_Sub1", "Activity_Sub2", "Activity_Sub3"]);
        process.FlowElements.OfType<SubProcess>().Single(p => p.Id == "Activity_Sub1").FlowElements
            .OfType<ServiceTask>()
            .Should().ContainSingle(t => t.Name == "Sub1_Step1");
        var subProcess = process.FlowElements.OfType<SubProcess>().Single(p => p.Id == "Activity_Sub2");
        subProcess.FlowElements.OfType<SubProcess>().Should().ContainSingle();
        subProcess.FlowElements.OfType<ServiceTask>().Should().ContainSingle();
        process.FlowElements.OfType<CallActivity>().Should().ContainSingle();
        process.FlowElements.OfType<SubProcess>().Count(p => p.LoopCharacteristics != null).Should().Be(1);
    }

    // Testzweck: Deckt den Fall „Parse Model Should Only Return Executable Processes“ ab.
    [Test]
    public async Task ParseModel_ShouldOnlyReturnExecutableProcesses()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             id="Definitions_ExecutableFilter">
                             <bpmn:process id="ExecutableProcess" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                             </bpmn:process>
                             <bpmn:process id="CallableButInactiveProcess" isExecutable="false">
                               <bpmn:startEvent id="StartEvent_2" />
                             </bpmn:process>
                             <bpmn:process id="MissingExecutableFlag">
                               <bpmn:startEvent id="StartEvent_3" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var model = await ModelParser.ParseModel(stream);

        model.GetProcesses()
            .Select(process => process.Id)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("ExecutableProcess");
    }

    // Testzweck: Deckt den Fall „Parse Model Should Parse Boundary Timer Event“ ab.
    [Test]
    public void ParseModel_ShouldParseBoundaryTimerEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                                             id="Definitions_BoundaryTimer"
                                             targetNamespace="http://bpmn.io/schema/bpmn">
                             <bpmn:process id="Process_BoundaryTimer" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:outgoing>Flow_1</bpmn:outgoing>
                               </bpmn:startEvent>
                               <bpmn:serviceTask id="Activity_1" name="Wait for boundary timer">
                                 <bpmn:extensionElements>
                                   <zeebe:taskDefinition type="wait-for-boundary" />
                                 </bpmn:extensionElements>
                                 <bpmn:incoming>Flow_1</bpmn:incoming>
                                 <bpmn:outgoing>Flow_2</bpmn:outgoing>
                               </bpmn:serviceTask>
                               <bpmn:boundaryEvent id="BoundaryTimer_1" cancelActivity="false" attachedToRef="Activity_1">
                                 <bpmn:outgoing>Flow_3</bpmn:outgoing>
                                 <bpmn:timerEventDefinition id="TimerDefinition_1">
                                   <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT5S</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:boundaryEvent>
                               <bpmn:endEvent id="EndEvent_Main">
                                 <bpmn:incoming>Flow_2</bpmn:incoming>
                               </bpmn:endEvent>
                               <bpmn:endEvent id="EndEvent_Boundary">
                                 <bpmn:incoming>Flow_3</bpmn:incoming>
                               </bpmn:endEvent>
                               <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="Activity_1" />
                               <bpmn:sequenceFlow id="Flow_2" sourceRef="Activity_1" targetRef="EndEvent_Main" />
                               <bpmn:sequenceFlow id="Flow_3" sourceRef="BoundaryTimer_1" targetRef="EndEvent_Boundary" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var model = ModelParser.ParseModel(xml);
        var process = model.GetProcesses().Single();
        var boundaryTimer = process.FlowElements.OfType<FlowzerBoundaryTimerEvent>().Should().ContainSingle().Subject;

        using (new AssertionScope())
        {
            boundaryTimer.Id.Should().Be("BoundaryTimer_1");
            boundaryTimer.CancelActivity.Should().BeFalse();
            boundaryTimer.AttachedToRef.Id.Should().Be("Activity_1");
            boundaryTimer.TimerType.Should().Be(FlowzerTimerType.TimeDuration);
            boundaryTimer.TimerDefinition.TimeDuration!.Body.Should().Be("PT5S");
        }
    }

    // Testzweck: Prüft, dass User-Tasks ihre Implementierung auch aus formId lesen, wenn kein formKey vorhanden ist.
    [Test]
    public void ParseModel_ShouldUseFormId_WhenUserTaskHasNoFormKey()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_UserTaskFormId">
                             <bpmn:process id="Process_UserTaskFormId" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:userTask id="Activity_UserTask">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formId="Form_InvoiceReview" />
                                 </bpmn:extensionElements>
                               </bpmn:userTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var model = ModelParser.ParseModel(xml);
        var userTask = model.GetProcesses().Single().FlowElements.OfType<UserTask>().Should().ContainSingle().Subject;

        userTask.Implementation.Should().Be("Form_InvoiceReview");
    }

    // Testzweck: Prüft, dass der Parser für User-Tasks ohne formKey/formId eine fachlich präzise Parser-Exception auslöst.
    [Test]
    public void ParseModel_ShouldThrowFlowzerModelParseException_WhenUserTaskHasNoImplementation()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_UserTaskMissingImplementation">
                             <bpmn:process id="Process_UserTaskMissingImplementation" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:userTask id="Activity_UserTask">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition />
                                 </bpmn:extensionElements>
                               </bpmn:userTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var action = () => ModelParser.ParseModel(xml);

        action.Should()
            .Throw<FlowzerModelParseException>()
            .WithMessage("User task 'Activity_UserTask'*");
    }

    // Testzweck: Prüft, dass ein im Workflow eingebettetes Formular (zeebe:userTaskForm in den
    // extensionElements des Prozesses) als Teil des Modells gelesen wird und der User-Task über
    // Camundas Form-Key-Präfix darauf verweist.
    [Test]
    public void ParseModel_ShouldReadEmbeddedUserTaskForm_WhenProcessCarriesUserTaskForm()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_EmbeddedForm">
                             <bpmn:process id="Process_EmbeddedForm" isExecutable="true">
                               <bpmn:extensionElements>
                                 <zeebe:userTaskForm id="UserTaskForm_Urlaub">{"display":"form","components":[]}</zeebe:userTaskForm>
                               </bpmn:extensionElements>
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:userTask id="Activity_UserTask">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formKey="camunda-forms:bpmn:UserTaskForm_Urlaub" />
                                 </bpmn:extensionElements>
                               </bpmn:userTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var process = ModelParser.ParseModel(xml).GetProcesses().Single();
        var userTask = process.FlowElements.OfType<UserTask>().Should().ContainSingle().Subject;

        using (new AssertionScope())
        {
            userTask.Implementation.Should().Be("camunda-forms:bpmn:UserTaskForm_Urlaub");
            var form = process.FlowzerUserTaskForms.Should().ContainSingle().Subject;
            form.Id.Should().Be("UserTaskForm_Urlaub");
            form.Schema.Should().Be("""{"display":"form","components":[]}""");
        }
    }

    // Testzweck: Prüft, dass nur die Formulare des Prozesses selbst gelesen werden. Ein
    // gleichnamiges Element in einem Flow-Element darf nicht als Prozessformular gelten,
    // sonst verwiese ein Form-Key auf ein Formular, das gar nicht zum Prozess gehört.
    [Test]
    public void ParseModel_ShouldIgnoreUserTaskForm_WhenItIsNotDirectlyOnTheProcess()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_MisplacedForm">
                             <bpmn:process id="Process_MisplacedForm" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                               <bpmn:userTask id="Activity_UserTask">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formKey="camunda-forms:bpmn:UserTaskForm_Falsch" />
                                   <zeebe:userTaskForm id="UserTaskForm_Falsch">{}</zeebe:userTaskForm>
                                 </bpmn:extensionElements>
                               </bpmn:userTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var process = ModelParser.ParseModel(xml).GetProcesses().Single();

        process.FlowzerUserTaskForms.Should().BeEmpty();
    }

    // Testzweck: Prüft, dass die Wiederholungen eines Service-Tasks (zeebe:taskDefinition/@retries)
    // ins Modell übernommen werden. Ohne sie liefe jeder Auftrag mit dem Standardwert, egal was
    // im Diagramm steht.
    [Test]
    public void ParseModel_ShouldReadServiceTaskRetries()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_Retries">
                             <bpmn:process id="Process_Retries" isExecutable="true">
                               <bpmn:serviceTask id="Activity_MitWiederholungen">
                                 <bpmn:extensionElements>
                                   <zeebe:taskDefinition type="rechnung-pruefen" retries="5" />
                                 </bpmn:extensionElements>
                               </bpmn:serviceTask>
                               <bpmn:serviceTask id="Activity_OhneAngabe">
                                 <bpmn:extensionElements>
                                   <zeebe:taskDefinition type="rechnung-buchen" />
                                 </bpmn:extensionElements>
                               </bpmn:serviceTask>
                               <bpmn:serviceTask id="Activity_MitAusdruck">
                                 <bpmn:extensionElements>
                                   <zeebe:taskDefinition type="rechnung-melden" retries="=versuche" />
                                 </bpmn:extensionElements>
                               </bpmn:serviceTask>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var serviceTasks = ModelParser.ParseModel(xml).GetProcesses().Single()
            .FlowElements.OfType<ServiceTask>().ToDictionary(task => task.Id);

        using (new AssertionScope())
        {
            serviceTasks["Activity_MitWiederholungen"].FlowzerRetries.Should().Be(5);
            serviceTasks["Activity_OhneAngabe"].FlowzerRetries.Should().Be(0);
            // Ein FEEL-Ausdruck lässt sich ohne Prozessdaten nicht auflösen; der Standardwert
            // ist ehrlicher als eine erfundene Zahl.
            serviceTasks["Activity_MitAusdruck"].FlowzerRetries.Should().Be(0);
        }
    }

    private static void AssertFlowNodeOfTypes<T>(Process process, int? count, string? id = null, string? name = null)
        where T : FlowElement
    {
        if (count is not null)
            Assert.That(process.FlowElements.Count(element => element.GetType() == typeof(T)), Is.EqualTo(count), $"{typeof(T).Name} Count");
        if (id is not null)
            Assert.That(process.FlowElements.OfType<T>().Select(fe => fe.Id), Has.One.AnyOf(id),
                $"{typeof(T).Name}.Id");
        if (name is not null)
            Assert.That(process.FlowElements.OfType<T>().Select(fe => fe.Name), Has.One.AnyOf(name),
                $"{typeof(T).Name}.Name");
        if (count is null && id is null && name is null) Assert.Fail("No assertion specified");
    }

    // Testzweck: Prüft, dass der Parser den Form-Key eines Startformulars am reinen
    // Startereignis liest. Ohne ihn wüsste die API nicht, welches Formular vor dem Start
    // auszufüllen ist.
    [Test]
    public void ParseModel_ShouldReadStartFormKey_WhenPlainStartEventCarriesFormDefinition()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_StartForm">
                             <bpmn:process id="Process_StartForm" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formKey="Urlaubsantrag" />
                                 </bpmn:extensionElements>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var startEvent = ModelParser.ParseModel(xml).GetProcesses().Single()
            .FlowElements.OfType<StartEvent>().Should().ContainSingle().Subject;

        startEvent.FlowzerFormKey.Should().Be("Urlaubsantrag");
    }

    // Testzweck: Prüft, dass ein Startereignis ohne formDefinition keinen Form-Key trägt.
    // Ein Workflow ohne Startformular soll wie bisher ohne Eingabe starten.
    [Test]
    public void ParseModel_ShouldLeaveStartFormKeyNull_WhenStartEventHasNoFormDefinition()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_NoStartForm">
                             <bpmn:process id="Process_NoStartForm" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1" />
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var startEvent = ModelParser.ParseModel(xml).GetProcesses().Single()
            .FlowElements.OfType<StartEvent>().Should().ContainSingle().Subject;

        startEvent.FlowzerFormKey.Should().BeNull();
    }

    // Testzweck: Prüft, dass auch am Startereignis formId als Ersatz für formKey gilt —
    // dieselbe Lesereihenfolge wie am User-Task, sonst zeigte dasselbe Diagramm je nach
    // Elementart auf ein anderes Formular.
    [Test]
    public void ParseModel_ShouldUseFormId_WhenStartEventHasNoFormKey()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_StartFormId">
                             <bpmn:process id="Process_StartFormId" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formId="Form_Antrag" />
                                 </bpmn:extensionElements>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var startEvent = ModelParser.ParseModel(xml).GetProcesses().Single()
            .FlowElements.OfType<StartEvent>().Should().ContainSingle().Subject;

        startEvent.FlowzerFormKey.Should().Be("Form_Antrag");
    }

    // Testzweck: Prüft, dass ein leerer Form-Key wie „kein Formular" gilt. Ein Leerraum wäre
    // sonst ein Verweis, den niemand auflösen kann, und der Start bräche ohne Grund ab.
    [Test]
    public void ParseModel_ShouldLeaveStartFormKeyNull_WhenFormKeyIsBlank()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_BlankStartForm">
                             <bpmn:process id="Process_BlankStartForm" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formKey="   " />
                                 </bpmn:extensionElements>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var startEvent = ModelParser.ParseModel(xml).GetProcesses().Single()
            .FlowElements.OfType<StartEvent>().Should().ContainSingle().Subject;

        startEvent.FlowzerFormKey.Should().BeNull();
    }

    // Testzweck: Prüft, dass ein formDefinition an einem Timer-Startereignis stillschweigend
    // ignoriert wird. bpmn-js behält die extensionElements beim Wechsel des Ereignistyps —
    // ein Modellfehler daraus zu machen, machte solche Diagramme unspeicherbar.
    [Test]
    public void ParseModel_ShouldIgnoreFormDefinition_OnTimerStartEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_TimerStartForm">
                             <bpmn:process id="Process_TimerStartForm" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formKey="Urlaubsantrag" />
                                 </bpmn:extensionElements>
                                 <bpmn:timerEventDefinition id="Timer_1">
                                   <bpmn:timeDuration>PT5S</bpmn:timeDuration>
                                 </bpmn:timerEventDefinition>
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var process = ModelParser.ParseModel(xml).GetProcesses().Single();
        var timerStart = process.FlowElements.OfType<FlowzerTimerStartEvent>().Should().ContainSingle().Subject;

        timerStart.FlowzerFormKey.Should().BeNull();
    }

    // Testzweck: Prüft, dass ein formDefinition an einem Nachrichten-Startereignis
    // stillschweigend ignoriert wird. Eine eintreffende Nachricht startet den Workflow ohne
    // Zutun — es gäbe niemanden, der das Formular ausfüllen könnte.
    [Test]
    public void ParseModel_ShouldIgnoreFormDefinition_OnMessageStartEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_MessageStartForm">
                             <bpmn:message id="Message_1" name="OrderCreated" />
                             <bpmn:process id="Process_MessageStartForm" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formKey="message-start-form" />
                                 </bpmn:extensionElements>
                                 <bpmn:messageEventDefinition id="MessageEventDefinition_1" messageRef="Message_1" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var process = ModelParser.ParseModel(xml).GetProcesses().Single();
        var messageStart = process.FlowElements.OfType<FlowzerMessageStartEvent>().Should().ContainSingle().Subject;

        messageStart.FlowzerFormKey.Should().BeNull();
    }

    // Testzweck: Prüft, dass ein formDefinition an einem Signal-Startereignis stillschweigend
    // ignoriert wird — dieselbe Begründung wie beim Nachrichtenstart.
    [Test]
    public void ParseModel_ShouldIgnoreFormDefinition_OnSignalStartEvent()
    {
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                             xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                             id="Definitions_SignalStartForm">
                             <bpmn:signal id="Signal_1" name="InventoryRefresh" />
                             <bpmn:process id="Process_SignalStartForm" isExecutable="true">
                               <bpmn:startEvent id="StartEvent_1">
                                 <bpmn:extensionElements>
                                   <zeebe:formDefinition formKey="signal-start-form" />
                                 </bpmn:extensionElements>
                                 <bpmn:signalEventDefinition id="SignalEventDefinition_1" signalRef="Signal_1" />
                               </bpmn:startEvent>
                             </bpmn:process>
                           </bpmn:definitions>
                           """;

        var process = ModelParser.ParseModel(xml).GetProcesses().Single();
        var signalStart = process.FlowElements.OfType<FlowzerSignalStartEvent>().Should().ContainSingle().Subject;

        signalStart.FlowzerFormKey.Should().BeNull();
    }

    // Testzweck: Prueft, dass das Beispiel „Urlaubsantrag" seinen Antrag ueber ein
    // Startformular stellt. Der frueher dafuer zustaendige User-Task „Urlaubsantrag stellen"
    // darf nicht wieder auftauchen, sonst waere der Antrag zweimal auszufuellen.
    [Test]
    public async Task ParseModel_ShouldReadTheStartFormOfTheUrlaubsantragExample()
    {
        await using var file = File.OpenRead(Path.Combine("examples", "urlaubsantrag.bpmn"));
        var process = (await ModelParser.ParseModel(file)).GetProcesses().Single();

        using (new AssertionScope())
        {
            process.FlowElements.OfType<StartEvent>().Should().ContainSingle()
                .Which.FlowzerFormKey.Should().Be("Urlaubsantrag");
            process.FlowElements.OfType<UserTask>()
                .Should().NotContain(task => task.Name == "Urlaubsantrag stellen");
        }
    }

    // Testzweck: Prueft, dass das Beispiel „Urlaubsantrag" genau ein abbrechendes Ende
    // traegt. Ohne es liefen die beiden anderen Pruefungen nach einer Ablehnung weiter und
    // haetten offene Aufgaben in den Listen stehen; ein zweites abbrechendes Ende waere ein
    // Versehen, denn der genehmigte Weg soll ganz normal zu Ende laufen.
    [Test]
    public async Task ParseModel_ShouldReadExactlyOneTerminateEndEventOfTheUrlaubsantragExample()
    {
        await using var file = File.OpenRead(Path.Combine("examples", "urlaubsantrag.bpmn"));
        var process = (await ModelParser.ParseModel(file)).GetProcesses().Single();

        using (new AssertionScope())
        {
            process.FlowElements.OfType<FlowzerTerminateEvent>().Should().ContainSingle()
                .Which.Name.Should().Be("Antrag abgelehnt");
            process.FlowElements.OfType<EndEvent>().Where(end => end.Name == "Urlaub genehmigt")
                .Should().ContainSingle().Which.Should().NotBeOfType<FlowzerTerminateEvent>();
        }
    }

    // Testzweck: Bestehende BPMN-Dateien ohne Flowzer-Erweiterung bleiben im Textmodus und
    // behalten die bisherigen Zeebe-Zuweisungen unverändert.
    [Test]
    public void ParseModel_ShouldKeepLegacyAssignmentsInTextMode()
    {
        var task = ParseAssignedUserTask("""
            <zeebe:assignmentDefinition assignee="anna" candidateUsers="bert,carla" candidateGroups="/team/finance" />
            """);

        using (new AssertionScope())
        {
            task.FlowzerAssignmentMode.Should().Be(UserTaskAssignmentMode.Text);
            task.FlowzerAssignee.Should().Be("anna");
            task.FlowzerCandidateUsers.Should().Be("bert,carla");
            task.FlowzerCandidateGroups.Should().Be("/team/finance");
            task.FlowzerDirectoryAssigneeUserId.Should().BeNull();
            task.FlowzerDirectoryCandidateUserIds.Should().BeEmpty();
            task.FlowzerDirectoryCandidateGroupIds.Should().BeEmpty();
        }
    }

    // Testzweck: Der Verzeichnismodus liest stabile Benutzer- und Gruppen-IDs typgerecht,
    // ohne sie in die weiterhin freien Textfelder zu kopieren.
    [Test]
    public void ParseModel_ShouldReadDirectoryAssignmentReferences()
    {
        var assignee = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var candidate = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var group = Guid.Parse("20000000-0000-0000-0000-000000000001");

        var task = ParseAssignedUserTask($$"""
            <flowzer:taskAssignment mode="directory" assigneeId="{{assignee}}"
              candidateUserIds="{{candidate}}" candidateGroupIds="{{group}}" />
            """);

        using (new AssertionScope())
        {
            task.FlowzerAssignmentMode.Should().Be(UserTaskAssignmentMode.Directory);
            task.FlowzerDirectoryAssigneeUserId.Should().Be(assignee);
            task.FlowzerDirectoryCandidateUserIds.Should().Equal(candidate);
            task.FlowzerDirectoryCandidateGroupIds.Should().Equal(group);
            task.FlowzerAssignee.Should().BeNull();
            task.FlowzerCandidateUsers.Should().BeNull();
            task.FlowzerCandidateGroups.Should().BeNull();
        }
    }

    // Testzweck: Ungültige, leere, doppelte oder mit Freitext vermischte Verzeichnisverträge
    // werden bereits beim Parsen verständlich abgelehnt und erreichen die Laufzeit nie.
    [TestCaseSource(nameof(InvalidDirectoryAssignments))]
    public void ParseModel_ShouldRejectInvalidAssignmentContracts(string extensionElements)
    {
        var action = () => ParseAssignedUserTask(extensionElements);

        action.Should().Throw<ModelValidationException>().WithMessage("*assignment*");
    }

    private static IEnumerable<TestCaseData> InvalidDirectoryAssignments()
    {
        yield return new TestCaseData("""<flowzer:taskAssignment mode="known" assigneeId="10000000-0000-0000-0000-000000000001" />""")
            .SetName("Unknown_assignment_mode");
        yield return new TestCaseData("""<flowzer:taskAssignment mode="directory" />""")
            .SetName("Directory_mode_without_references");
        yield return new TestCaseData("""<flowzer:taskAssignment mode="directory" assigneeId="not-a-guid" />""")
            .SetName("Malformed_directory_reference");
        yield return new TestCaseData("""<flowzer:taskAssignment mode="directory" candidateUserIds="10000000-0000-0000-0000-000000000001,10000000-0000-0000-0000-000000000001" />""")
            .SetName("Duplicate_directory_reference");
        yield return new TestCaseData("""
            <zeebe:assignmentDefinition assignee="anna" />
            <flowzer:taskAssignment mode="directory" assigneeId="10000000-0000-0000-0000-000000000001" />
            """).SetName("Directory_mode_mixed_with_legacy_text");
        yield return new TestCaseData("""<flowzer:taskAssignment mode="text" assigneeId="10000000-0000-0000-0000-000000000001" />""")
            .SetName("Text_mode_with_directory_reference");
        yield return new TestCaseData("""
            <flowzer:taskAssignment mode="directory" assigneeId="10000000-0000-0000-0000-000000000001" />
            <flowzer:taskAssignment mode="directory" assigneeId="10000000-0000-0000-0000-000000000002" />
            """).SetName("Multiple_assignment_contracts");
        yield return new TestCaseData("""<other:taskAssignment xmlns:other="https://example.test/wrong" mode="directory" assigneeId="10000000-0000-0000-0000-000000000001" />""")
            .SetName("Assignment_contract_in_wrong_namespace");
        yield return new TestCaseData("""<flowzer:taskAssignment mode="directory" assigneeId="10000000-0000-0000-0000-000000000001" candidateUserId="10000000-0000-0000-0000-000000000002" />""")
            .SetName("Unknown_assignment_attribute");
    }

    private static UserTask ParseAssignedUserTask(string assignmentXml)
    {
        var xml = $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                              xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                              id="Definitions_Assignment">
              <bpmn:process id="Process_Assignment" isExecutable="true">
                <bpmn:startEvent id="Start" />
                <bpmn:userTask id="Task">
                  <bpmn:extensionElements>
                    <zeebe:formDefinition formKey="Approval" />
                    {{assignmentXml}}
                  </bpmn:extensionElements>
                </bpmn:userTask>
              </bpmn:process>
            </bpmn:definitions>
            """;

        return ModelParser.ParseModel(xml).GetProcesses().Single()
            .FlowElements.OfType<UserTask>().Single();
    }
}
