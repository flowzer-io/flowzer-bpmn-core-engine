using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using core_engine;

namespace Examples;

/// <summary>
/// Minimales Nutzungsbeispiel für den öffentlichen <see cref="ICore"/>-Vertrag.
/// </summary>
public class SimpleEngineExample
{
    public async Task RunSimpleApprovalProcess()
    {
        var engine = new CoreEngine(FlowzerConfig.CreateForTests());
        SetupEventHandlers(engine);

        await using var xmlStream = File.OpenRead("examples/simple-approval-process.bpmn");
        await engine.LoadBpmnFile(xmlStream, verify: true);

        var subscriptions = await engine.GetInitialSubscriptions();
        var startSubscription = subscriptions.Single();

        Console.WriteLine($"Start-Subscription: {startSubscription.Type} ({startSubscription.BpmnNodeId})");

        var instanceId = Guid.NewGuid();
        var startResult = await engine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = startSubscription.BpmnNodeId
        });

        Console.WriteLine($"Instanz gestartet: {startResult.Instance.Id}");
        await ContinueInteractions(engine, instanceId, startResult.Instance.Interactions);
    }

    /// <summary>
    /// Registriert einen einfachen Konsolen-Handler für Statuswechsel.
    /// </summary>
    private static void SetupEventHandlers(ICore engine)
    {
        engine.InteractionFinished += (_, instance) =>
        {
            Console.WriteLine(
                $"InteractionFinished: Instanz={instance.Id}, State={instance.State}, offene Interaktionen={instance.Interactions.Count}");
        };
    }

    private static async Task ContinueInteractions(
        ICore engine,
        Guid instanceId,
        IReadOnlyList<CoreInteraction> interactions)
    {
        var pendingInteractions = interactions;

        while (pendingInteractions.Count > 0)
        {
            var interaction = pendingInteractions[0];
            Console.WriteLine($"Bearbeite {interaction.Type}: {interaction.NodeId} ({interaction.Name})");

            var additionalData = interaction.Type == CoreInteractionType.UserTask
                ? new Dictionary<string, object?> { ["approval"] = "approved" }
                : new Dictionary<string, object?>();

            var result = await engine.HandleEvent(new CoreEventData
            {
                InstanceId = instanceId,
                BpmnNodeId = interaction.NodeId,
                AdditionalData = additionalData
            });

            pendingInteractions = result.Instance.Interactions;
        }
    }
}

/// <summary>
/// Einfache Startklasse für das Beispiel.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Flowzer BPMN Core Engine – ICore-Beispiel");
        Console.WriteLine("========================================");

        try
        {
            var example = new SimpleEngineExample();
            await example.RunSimpleApprovalProcess();

            Console.WriteLine();
            Console.WriteLine("Beispiel erfolgreich abgeschlossen.");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Fehler beim Ausführen des Beispiels: {ex.Message}");
            Console.WriteLine(ex);
        }
    }
}
