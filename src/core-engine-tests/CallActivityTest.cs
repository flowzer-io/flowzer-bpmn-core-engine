using System.Dynamic;
using BPMN.Process;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Die lokale Call Activity aus Sicht der Engine: Ein Token wartet an ihr wie an einem
/// Service-Task und die Engine stellt den ausstehenden Aufruf samt Eingabevariablen bereit.
/// Die Kindinstanz zu starten ist Sache des Aufrufers — die Engine kennt keine anderen
/// Instanzen. Zurueck kommt entweder ein Ergebnis oder ein BPMN-Fehler.
/// </summary>
public class CallActivityTest
{
    // Testzweck: Ein Token an einer Call Activity wartet und die Engine stellt genau einen
    // ausstehenden Aufruf mit Zielprozess und den nach zeebe:ioMapping gebundenen Eingaben bereit.
    [Test]
    public async Task CallActivity_ShouldWaitAndOfferPendingCallWithMappedInput()
    {
        var instance = await StartWithVariables("CallActivityWithMapping.bpmn", new
        {
            antragsnummer = "4711",
            intern = "bleibt hier"
        });

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);

            var waiting = instance.GetWaitingCallActivities().Should().ContainSingle().Which;
            waiting.CurrentBaseElement.Id.Should().Be("Call_1");
            waiting.State.Should().Be(FlowNodeState.Active);

            var call = instance.PendingCallActivities.Should().ContainSingle().Which;
            call.TokenId.Should().Be(waiting.Id);
            call.ProcessId.Should().Be("second-level-support");

            var variables = (IDictionary<string, object?>)call.Variables;
            variables.Should().HaveCount(1);
            variables["ticketNummer"].Should().Be("4711");
        }
    }

    // Testzweck: Ohne Eingangszuordnung, aber mit propagateAllParentVariables reicht die Engine
    // den ganzen Prozesskontext an den aufgerufenen Prozess weiter.
    [Test]
    public async Task CallActivityWithPropagateAllParentVariables_ShouldPassWholeProcessContext()
    {
        var instance = await StartWithVariables("CallActivityPropagateAll.bpmn", new
        {
            antragsnummer = "4711",
            entscheidung = "offen"
        });

        var call = instance.PendingCallActivities.Should().ContainSingle().Which;
        var variables = (IDictionary<string, object?>)call.Variables;

        using (new AssertionScope())
        {
            call.ProcessId.Should().Be("teilvorgang");
            variables["antragsnummer"].Should().Be("4711");
            variables["entscheidung"].Should().Be("offen");
        }
    }

    // Testzweck: Ein ausstehender Aufruf wird genau einmal herausgegeben; ein zweiter Engine-Lauf
    // darf denselben Aufruf nicht erneut anbieten und damit eine zweite Kindinstanz erzeugen.
    [Test]
    public async Task TakePendingCallActivities_ShouldHandOutEveryCallOnlyOnce()
    {
        var instance = await StartWithVariables("CallActivityPropagateAll.bpmn", new { antragsnummer = "4711" });

        var taken = instance.TakePendingCallActivities();
        // Ein weiterer Lauf der Engine, wie ihn jede spaetere Mutation ausloest.
        instance.HandleTime(DateTime.UtcNow);

        using (new AssertionScope())
        {
            taken.Should().ContainSingle();
            instance.PendingCallActivities.Should().BeEmpty();
        }
    }

    // Testzweck: Mit zeebe:ioMapping-Ausgang uebernimmt der Elternprozess genau die zugeordneten
    // Werte des Kindes — und nicht dessen ganzen Variablenstand.
    [Test]
    public async Task CompleteCallActivity_ShouldApplyOutputMapping()
    {
        var instance = await StartWithVariables("CallActivityWithMapping.bpmn", new { antragsnummer = "4711" });
        var call = instance.PendingCallActivities.Single();

        instance.CompleteCallActivity(call.TokenId, AsVariables(new
        {
            loesung = "Kabel getauscht",
            internesProtokoll = "bleibt im Kind"
        }));

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            var processVariables = (IDictionary<string, object?>)instance.MasterToken.Variables!;
            processVariables["loesungSecondLevel"].Should().Be("Kabel getauscht");
            processVariables.Should().NotContainKey("internesProtokoll");
            processVariables.Should().NotContainKey("loesung");
        }
    }

    // Testzweck: Ohne Ausgangszuordnung, aber mit propagateAllChildVariables uebernimmt der
    // Elternprozess den gesamten Variablenstand des Kindes.
    [Test]
    public async Task CompleteCallActivity_WithPropagateAllChildVariables_ShouldTakeEveryChildVariable()
    {
        var instance = await StartWithVariables("CallActivityPropagateAll.bpmn", new { antragsnummer = "4711" });
        var call = instance.PendingCallActivities.Single();

        instance.CompleteCallActivity(call.TokenId, AsVariables(new { loesung = "Kabel getauscht" }));

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            ((IDictionary<string, object?>)instance.MasterToken.Variables!)["loesung"].Should().Be("Kabel getauscht");
        }
    }

    // Testzweck: Ein Fehler des aufgerufenen Prozesses erscheint als BPMN-Fehler an der Call
    // Activity und wird dort vom Error-Boundary gefangen; die Elterninstanz laeuft weiter.
    [Test]
    public async Task FailedCallActivity_ShouldBeCaughtByErrorBoundary()
    {
        var instance = await Helper.StartFirstProcessOfFile("CallActivityWithErrorBoundary.bpmn");
        var call = instance.PendingCallActivities.Single();

        instance.ThrowBpmnError(call.TokenId, "BONITAET", "Der aufgerufene Prozess ist gescheitert.");

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instance.FailureReason.Should().BeNull();
            var recovering = instance.GetActiveServiceTasks().Should().ContainSingle().Which;
            recovering.CurrentBaseElement.Id.Should().Be("ServiceTask_Recover");
        }
    }

    // Testzweck: Ohne passendes Error-Boundary schlaegt der Fehler des aufgerufenen Prozesses bis
    // zur Prozessebene durch; die Elterninstanz scheitert mit nachvollziehbarer Begruendung.
    [Test]
    public async Task FailedCallActivityWithoutBoundary_ShouldFailTheCallingInstance()
    {
        var instance = await StartWithVariables("CallActivityPropagateAll.bpmn", new { antragsnummer = "4711" });
        var call = instance.PendingCallActivities.Single();

        instance.ThrowBpmnError(call.TokenId, "CALLED_PROCESS_NOT_FOUND");

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Failed);
            instance.FailureErrorCode.Should().Be("CALLED_PROCESS_NOT_FOUND");
            instance.FailureReason.Should().Contain("CALLED_PROCESS_NOT_FOUND").And.Contain("Call_1");
        }
    }

    // Testzweck: Ein Abbruch von aussen bleibt am Tokenstand unterscheidbar von einem regulaeren
    // Ende — nur so kann der Aufrufer einen abgebrochenen Kindvorgang anders behandeln.
    [Test]
    public async Task Cancel_ShouldMarkTheInstanceAsCancelled()
    {
        var instance = await StartWithVariables("CallActivityPropagateAll.bpmn", new { antragsnummer = "4711" });

        using (new AssertionScope())
        {
            instance.WasCancelled.Should().BeFalse();
            instance.Cancel();
            instance.WasCancelled.Should().BeTrue();
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Terminated);
        }
    }

    private static async Task<InstanceEngine> StartWithVariables(string fileName, object variables)
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/" + fileName, FileMode.Open));
        var processEngine = Helper.CreateProcessEngine(model.GetProcesses().First());

        return processEngine.StartProcess(AsVariables(variables));
    }

    private static ExpandoObject AsVariables(object source)
    {
        var variables = new ExpandoObject();
        var target = (IDictionary<string, object?>)variables;
        foreach (var property in source.GetType().GetProperties())
        {
            target[property.Name] = property.GetValue(source);
        }

        return variables;
    }
}
