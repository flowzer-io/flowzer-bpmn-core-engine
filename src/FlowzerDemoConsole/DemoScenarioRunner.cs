using System.Reflection;
using core_engine;

namespace FlowzerDemoConsole;

/// <summary>
/// Führt den dokumentierten Happy Path des öffentlichen <see cref="ICore"/>-Vertrags aus.
/// Die Klasse ist bewusst testbar gehalten, damit die Console-Demo nicht von der empfohlenen API-Nutzung wegdriftet.
/// </summary>
public sealed class DemoScenarioRunner
{
    private const string BpmnResourceName = "FlowzerDemoConsole.Assets.simple-approval-process.bpmn";

    // Statischer Beispieldatensatz für den dokumentierten Demo-Happy-Path.
    private static readonly IReadOnlyDictionary<string, object?> ApprovedDecisionData =
        new Dictionary<string, object?>
        {
            ["approval"] = "approved"
        };

    private static readonly IReadOnlyDictionary<string, object?> EmptyData =
        new Dictionary<string, object?>();

    /// <summary>
    /// Führt die Demo gegen das eingebettete BPMN-Beispiel aus und schreibt die wesentlichen Schritte in den angegebenen Writer.
    /// </summary>
    public async Task<CoreInstance> RunAsync(TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        var engine = new CoreEngine(FlowzerConfig.CreateForTests());
        engine.InteractionFinished += (_, instance) =>
        {
            output.WriteLine(
                $"InteractionFinished: Instanz={instance.Id}, State={instance.State}, offene Interaktionen={instance.Interactions.Count}");
        };

        await using var xmlStream = OpenEmbeddedBpmnStream();
        await engine.LoadBpmnFile(xmlStream, cancellationToken);

        var subscriptions = await engine.GetInitialSubscriptions(cancellationToken);
        if (subscriptions.Length != 1)
        {
            throw new InvalidOperationException(
                $"Die Demo erwartet genau eine Start-Subscription, gefunden wurden aber {subscriptions.Length}.");
        }

        var startSubscription = subscriptions.Single();
        output.WriteLine($"Start-Subscription: {startSubscription.Type} ({startSubscription.BpmnNodeId})");

        var instanceId = Guid.NewGuid();
        var currentResult = await engine.HandleEvent(new CoreEventData
        {
            InstanceId = instanceId,
            BpmnNodeId = startSubscription.BpmnNodeId
        }, cancellationToken);

        output.WriteLine($"Instanz gestartet: {currentResult.Instance.Id}");

        while (currentResult.Instance.Interactions.Count > 0)
        {
            var interaction = currentResult.Instance.Interactions[0];
            output.WriteLine($"Bearbeite {interaction.Type}: {interaction.NodeId} ({interaction.Name})");

            currentResult = await engine.HandleEvent(new CoreEventData
            {
                InstanceId = instanceId,
                BpmnNodeId = interaction.NodeId,
                InteractionId = interaction.InteractionId,
                AdditionalData = BuildAdditionalData(interaction)
            }, cancellationToken);
        }

        output.WriteLine($"Prozess abgeschlossen: {currentResult.Instance.State}");
        return currentResult.Instance;
    }

    private static IReadOnlyDictionary<string, object?> BuildAdditionalData(CoreInteraction interaction)
    {
        return interaction.Type == CoreInteractionType.UserTask
            ? ApprovedDecisionData
            : EmptyData;
    }

    private static Stream OpenEmbeddedBpmnStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(BpmnResourceName);
        if (stream == null)
        {
            throw new FileNotFoundException(
                $"Die eingebettete Demo-BPMN-Ressource '{BpmnResourceName}' konnte nicht geladen werden.");
        }

        return stream;
    }
}
