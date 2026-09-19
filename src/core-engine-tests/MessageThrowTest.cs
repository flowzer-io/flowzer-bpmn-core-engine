using System.Dynamic;
using BPMN.Flowzer;
using BPMN.Process;
using Flowzer.Shared;
using FluentAssertions;
using FluentAssertions.Execution;
using Model;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>
/// Sendende Nachrichtenelemente: Die Engine legt die Nachricht mit ausgewertetem
/// Korrelationsschlüssel und den gemappten Eingabewerten bereit und läuft sofort weiter.
/// Zustellen ist Sache des Aufrufers; die Engine kennt keine anderen Instanzen.
/// </summary>
public class MessageThrowTest
{
    // Testzweck: Ein Message-Throw-Event sammelt genau eine ausgehende Nachricht mit dem gegen
    // die Prozessvariablen ausgewerteten Korrelationsschlüssel und läuft ohne zu warten weiter.
    [Test]
    public async Task MessageThrowEvent_ShouldCollectOutgoingMessageAndContinue()
    {
        var instance = await StartWithVariables("MessageThrow.bpmn", new
        {
            antragsnummer = "4711",
            entscheidung = "genehmigt",
            intern = "bleibt hier"
        });

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);

            var message = instance.OutgoingMessages.Should().ContainSingle().Which;
            message.Name.Should().Be("Freigabe erteilt");
            message.CorrelationKey.Should().Be("4711");

            var variables = VariablesOf(message);
            variables.Should().HaveCount(2);
            variables["antragsnummer"].Should().Be("4711");
            variables["entscheidung"].Should().Be("genehmigt");
        }
    }

    // Testzweck: Ohne zeebe:ioMapping sendet Flowzer bewusst keine Variablen mit, statt den
    // ganzen Prozesskontext an einen fremden Empfänger zu geben.
    [Test]
    public async Task MessageThrowEventWithoutMapping_ShouldSendNoVariables()
    {
        var instance = await StartWithVariables("MessageThrowWithoutMapping.bpmn", new
        {
            antragsnummer = "4711",
            geheim = "darf nicht mitgehen"
        });

        using (new AssertionScope())
        {
            var message = instance.OutgoingMessages.Should().ContainSingle().Which;
            message.CorrelationKey.Should().Be("4711");
            VariablesOf(message).Should().BeEmpty();
        }
    }

    // Testzweck: Ein Message-End-Event sendet wie ein Throw und beendet danach seinen Pfad.
    [Test]
    public async Task MessageEndEvent_ShouldSendAndEndThePath()
    {
        var instance = await StartWithVariables("MessageEnd.bpmn", new { antragsnummer = "4711" });

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instance.ActiveTokens.Should().BeEmpty();

            var message = instance.OutgoingMessages.Should().ContainSingle().Which;
            message.Name.Should().Be("Vorgang abgeschlossen");
            message.CorrelationKey.Should().Be("4711");
            VariablesOf(message)["antragsnummer"].Should().Be("4711");
        }
    }

    // Testzweck: Ein Throw-Event ohne Ereignisdefinition ist ein reiner Meilenstein: Es läuft
    // durch, bleibt im Tokenstand sichtbar und sendet nichts.
    [Test]
    public async Task PlainThrowEvent_ShouldPassThroughWithoutSending()
    {
        var instance = await Helper.StartFirstProcessOfFile("PlainThrowEvent.bpmn");

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
            instance.OutgoingMessages.Should().BeEmpty();
            instance.Tokens.Should().Contain(token => token.CurrentBaseElement.Id == "Milestone_1"
                && token.State == FlowNodeState.Completed);
        }
    }

    // Testzweck: Mit zeebe:taskDefinition wartet ein Send-Task wie ein Service-Task auf einen
    // externen Worker, statt selbst zu korrelieren.
    [Test]
    public async Task SendTaskWithTaskDefinition_ShouldWaitForWorkerInsteadOfCorrelating()
    {
        var instance = await Helper.StartFirstProcessOfFile("SendTaskAsJob.bpmn");

        using (new AssertionScope())
        {
            instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Waiting);
            instance.OutgoingMessages.Should().BeEmpty();

            var waiting = instance.GetActiveServiceTasks().Should().ContainSingle().Which;
            waiting.CurrentBaseElement.Id.Should().Be("Send_Job");
            ((IFlowzerWorkerTask)waiting.CurrentFlowNode!).Implementation.Should().Be("mail-versenden");
            ((IFlowzerWorkerTask)waiting.CurrentFlowNode!).FlowzerRetries.Should().Be(3);
        }

        instance.HandleTaskResult(instance.GetActiveServiceTasks().Single().Id, null);
        instance.ProcessInstanceState.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Abgeholte Nachrichten verschwinden aus der Liste, damit derselbe Wurf nicht
    // zweimal zugestellt wird.
    [Test]
    public async Task TakeOutgoingMessages_ShouldHandOverAndClearTheList()
    {
        var instance = await StartWithVariables("MessageThrow.bpmn", new
        {
            antragsnummer = "4711",
            entscheidung = "genehmigt"
        });

        using (new AssertionScope())
        {
            instance.TakeOutgoingMessages().Should().ContainSingle();
            instance.OutgoingMessages.Should().BeEmpty();
            instance.TakeOutgoingMessages().Should().BeEmpty();
        }
    }

    private static async Task<InstanceEngine> StartWithVariables(string fileName, object variables)
    {
        var model = await ModelParser.ParseModel(File.Open("embeddings/" + fileName, FileMode.Open));
        var process = model.GetProcesses().First();

        return Helper.CreateProcessEngine(process).StartProcess((ExpandoObject?)variables.ToDynamic());
    }

    private static IDictionary<string, object?> VariablesOf(Message message) =>
        JsonConvert.DeserializeObject<Dictionary<string, object?>>(message.Variables ?? "{}")!;
}
