using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using BPMN.Common;
using Model;

namespace core_engine_tests;

/// <summary>
/// Prüft das Original-Demomodell mit echter Kernengine, ohne HTTP, Host oder Datenbank.
/// Synthetische Workerresultate sind ausdrücklich kein Nachweis tatsächlicher TT-Ticketwirkungen.
/// </summary>
public sealed class TickyTaskTicketActionsExampleTest
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Zeebe = "http://camunda.org/schema/zeebe/1.0";

    // Testzweck: Jeder feste Aktionsknoten hat einen expliziten minimalen Eingang;
    // weder Rootdaten noch vom Antrag behauptete Ziel-/Benutzerrechte dürfen hineingelangen.
    [TestCase("Task_CreateTicket", "tt.ticket.create", false)]
    [TestCase("Task_ReadTicket", "tt.ticket.read", true)]
    [TestCase("Task_DelegateTicket", "tt.ticket.delegate", true)]
    [TestCase("Task_CloseTicket", "tt.ticket.close", true)]
    public void OriginalActionNode_ShouldDeclareOnlyTheClosedWorkerInputs(string nodeId, string type, bool createdTarget)
    {
        var document = XDocument.Parse(ReadOriginal());
        var node = document.Descendants(Bpmn + "serviceTask").Single(element => (string?)element.Attribute("id") == nodeId);
        var definition = node.Descendants(Zeebe + "taskDefinition").Single();
        var outputs = node.Descendants(Zeebe + "output").ToDictionary(element => (string)element.Attribute("target")!, element => (string)element.Attribute("source")!);
        var expectedOutputs = nodeId switch
        {
            "Task_CreateTicket" => new Dictionary<string, string> { ["createdTicketReference"] = "=ticketReference" },
            "Task_ReadTicket" => new Dictionary<string, string> { ["demoTicketId"] = "=idWithChecksum", ["demoTicketState"] = "=state" },
            "Task_DelegateTicket" => new Dictionary<string, string> { ["demoTicketDelegated"] = "=completed" },
            _ => new Dictionary<string, string> { ["demoTicketClosed"] = "=completed" },
        };
        var inputs = node.Descendants(Zeebe + "input").ToDictionary(element => (string)element.Attribute("target")!, element => (string)element.Attribute("source")!);
        Assert.Multiple(() =>
        {
            Assert.That((string?)definition.Attribute("type"), Is.EqualTo(type));
            Assert.That(inputs["operation"], Is.EqualTo("=\"execute\""));
            Assert.That(inputs.Keys, Is.EquivalentTo(createdTarget ? new[] { "operation", "ticketReference" } : ["operation"]));
            if (createdTarget) Assert.That(inputs["ticketReference"], Is.EqualTo("=createdTicketReference"));
            Assert.That(outputs, Is.EquivalentTo(expectedOutputs), "Resultate benötigen die feste minimale Projektion des wirklichen TT-Vertrags.");
        });
    }

    // Testzweck: Das separate Beispiel besitzt nur die vier bekannten Ticketaktionen;
    // keine versteckten Connectoren, KI-/Timer-Schritte oder dynamische Aktionsauswahl.
    [Test]
    public void OriginalDefinition_ShouldContainExactlyTheReviewedFourActions()
    {
        var document = XDocument.Parse(ReadOriginal());
        Assert.Multiple(() =>
        {
            Assert.That((string?)document.Root!.Attribute("id"), Is.EqualTo("tickytask-demo-ticketaktionen"));
            Assert.That(document.Descendants(Bpmn + "process").Count(), Is.EqualTo(1));
            Assert.That(document.Descendants(Bpmn + "serviceTask").Count(), Is.EqualTo(4));
            Assert.That(document.Descendants(Zeebe + "taskDefinition").Select(element => (string?)element.Attribute("type")),
                Is.EquivalentTo(new[] { "tt.ticket.create", "tt.ticket.read", "tt.ticket.delegate", "tt.ticket.close" }));
            Assert.That(document.Descendants().Where(element => element.Name.LocalName is "timerEventDefinition" or "scriptTask" or "sendTask" or "aiTask"), Is.Empty);
        });
    }

    // Testzweck: Die echte Engine durchläuft Anlage, explizites Lesen, Delegation und Abschluss;
    // nur der aus der Anlage projizierte opake Locator folgt weiter, nie Startdaten oder freie Resultatfelder.
    [Test]
    public void RealCoreEngine_ShouldRunTheOriginalSequenceWithClosedInputsAndOutputs()
    {
        var engine = Start();
        var reference = "e4c684bd-dd4a-4d6b-a085-de7ef42c1d3d";
        var actor = Guid.Parse("dd12a9a3-d2cd-4b36-a089-37e704171b0c");
        Complete("Task_CreateTicket", false, Values(("ticketReference", reference)));
        Complete("Task_ReadTicket", true, Values(("idWithChecksum", 1018), ("state", 0)));
        Complete("Task_DelegateTicket", true, Values(("completed", true)));
        Complete("Task_CloseTicket", true, Values(("completed", true)));
        var root = (IDictionary<string, object?>)engine.MasterToken.Variables!;
        Assert.Multiple(() =>
        {
            Assert.That(engine.ProcessInstanceState, Is.EqualTo(ProcessInstanceState.Completed));
            Assert.That(engine.GetActiveServiceTasks(), Is.Empty);
            Assert.That(root["createdTicketReference"], Is.EqualTo(reference));
            Assert.That(root["demoTicketId"], Is.EqualTo(1018));
            Assert.That(root["demoTicketState"], Is.EqualTo(0));
            Assert.That(root["demoTicketDelegated"], Is.EqualTo(true));
            Assert.That(root["demoTicketClosed"], Is.EqualTo(true));
            Assert.That(root.ContainsKey("unexpectedWorkerData"), Is.False);
            Assert.That(root.ContainsKey("UserId"), Is.False);
        });

        void Complete(string nodeId, bool createdTarget, ExpandoObject result)
        {
            var token = engine.GetActiveServiceTasks().Single();
            var input = (IDictionary<string, object?>)token.Variables!;
            Assert.Multiple(() =>
            {
                Assert.That(token.CurrentBaseElement.Id, Is.EqualTo(nodeId));
                Assert.That(input.Keys, Is.EquivalentTo(createdTarget ? new[] { "operation", "ticketReference" } : ["operation"]));
                Assert.That(input["operation"], Is.EqualTo("execute"));
                if (createdTarget) Assert.That(input["ticketReference"], Is.EqualTo(reference));
            });
            ((IDictionary<string, object?>)result)["unexpectedWorkerData"] = "nur synthetischer Fremdwert";
            engine.HandleTaskResult(token.Id, result, actor);
            Assert.That(token.CompletedByUserId, Is.EqualTo(actor));
        }
    }

    // Testzweck: Prozessabbruch beendet wartende Engineaufträge vor bzw. nach synthetischer Anlage;
    // daraus wird ausdrücklich weder ein TT-Rollback noch eine echte Job-/SQL-Abnahme abgeleitet.
    [TestCase(false)]
    [TestCase(true)]
    public void RealCoreEngine_Cancellation_ShouldRemoveAllRemainingActions(bool afterCreation)
    {
        var engine = Start();
        if (afterCreation)
            engine.HandleTaskResult(engine.GetActiveServiceTasks().Single().Id, Values(("ticketReference", "e4c684bd-dd4a-4d6b-a085-de7ef42c1d3d")));
        engine.Cancel();
        Assert.Multiple(() =>
        {
            Assert.That(engine.ProcessInstanceState, Is.EqualTo(ProcessInstanceState.Terminated));
            Assert.That(engine.GetActiveServiceTasks(), Is.Empty);
        });
    }

    private static InstanceEngine Start()
    {
        var process = ModelParser.ParseModel(ReadOriginal()).GetProcesses().Single();
        return Helper.CreateProcessEngine(process).StartProcess(Values(("sensitiveDemoStartData", "darf nie in einem Job stehen"), ("ticketReference", "manipulierter Startlocator")));
    }

    private static ExpandoObject Values(params (string Key, object? Value)[] values)
    {
        ExpandoObject result = new();
        foreach (var value in values) ((IDictionary<string, object?>)result)[value.Key] = value.Value;
        return result;
    }

    // Den Originalpfad an die aktuelle Quellfassung binden, keine duplizierte Test-BPMN.
    private static string ReadOriginal([CallerFilePath] string source = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../examples/tickytask-ticketaktionen/ticketaktionen.bpmn"));
        Assert.That(File.Exists(path), Is.True, "Der separate reviewbare Originalprozess fehlt.");
        return File.ReadAllText(path);
    }
}
