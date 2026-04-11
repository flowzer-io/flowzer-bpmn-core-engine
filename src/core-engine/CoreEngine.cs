using System.Text;
using core_engine.Extensions;
using Newtonsoft.Json;

namespace core_engine;

/// <summary>
/// Ein schlanker, in-memory Kernvertrag oberhalb von <see cref="ProcessEngine"/> und <see cref="InstanceEngine"/>.
/// Fokus der ersten Version sind ein geladener Prozess sowie Plain-Start-Events und aktive User-/Service-Tasks.
/// </summary>
public class CoreEngine(FlowzerConfig? flowzerConfig = null) : ICore
{
    private readonly Dictionary<Guid, InstanceEngine> _instances = new();
    private readonly FlowzerConfig _flowzerConfig = flowzerConfig ?? FlowzerConfig.Default;
    private Process? _process;

    public event EventHandler<CoreInstance>? InteractionFinished;

    public async System.Threading.Tasks.Task LoadBpmnFile(Stream xmlDataStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xmlDataStream);
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = new StreamReader(xmlDataStream, Encoding.UTF8, leaveOpen: true);
        var xml = await reader.ReadToEndAsync(cancellationToken);

        _process = null;
        _instances.Clear();

        var definitions = ModelParser.ParseModel(xml);
        var processes = definitions.GetProcesses().ToArray();

        if (processes.Length != 1)
        {
            throw new NotSupportedException("CoreEngine unterstützt aktuell genau einen Prozess pro geladener Definition.");
        }

        _process = processes[0];
        _instances.Clear();
    }

    public System.Threading.Tasks.Task<CoreSubscription[]> GetInitialSubscriptions(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startFlowNodes = GetLoadedProcess()
            .GetStartFlowNodes()
            .ToArray();
        var plainStartNodes = startFlowNodes
            .Where(node => node is StartEvent && node is not FlowzerMessageStartEvent && node is not FlowzerSignalStartEvent)
            .ToArray();

        var subscriptions = startFlowNodes
            .Where(node =>
                node is FlowzerMessageStartEvent ||
                node is FlowzerSignalStartEvent ||
                plainStartNodes.Length == 1 &&
                node is StartEvent &&
                node is not FlowzerMessageStartEvent &&
                node is not FlowzerSignalStartEvent)
            .Select(MapInitialSubscription)
            .ToArray();

        return System.Threading.Tasks.Task.FromResult(subscriptions);
    }

    public System.Threading.Tasks.Task<CoreEventResult> HandleEvent(CoreEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        cancellationToken.ThrowIfCancellationRequested();

        InstanceEngine instanceEngine;
        if (_instances.TryGetValue(eventData.InstanceId, out var existingInstance))
        {
            instanceEngine = existingInstance;
            CompleteInteraction(instanceEngine, eventData);
        }
        else
        {
            instanceEngine = StartNewInstance(eventData);
            _instances[eventData.InstanceId] = instanceEngine;
        }

        var snapshot = CreateInstanceSnapshot(eventData.InstanceId, instanceEngine);
        InteractionFinished?.Invoke(this, snapshot);
        if (instanceEngine.IsFinished)
        {
            _instances.Remove(eventData.InstanceId);
        }

        return System.Threading.Tasks.Task.FromResult(new CoreEventResult
        {
            Instance = snapshot
        });
    }

    private InstanceEngine StartNewInstance(CoreEventData eventData)
    {
        var process = GetLoadedProcess();
        var selectedStartNode = process
            .GetStartFlowNodes()
            .SingleOrDefault(node => string.Equals(node.Id, eventData.BpmnNodeId, StringComparison.Ordinal));

        if (selectedStartNode == null)
        {
            throw new InvalidOperationException(
                $"Das Start-Event \"{eventData.BpmnNodeId}\" wurde in der geladenen Definition nicht gefunden.");
        }

        return selectedStartNode switch
        {
            FlowzerMessageStartEvent messageStartEvent => StartNewMessageInstance(process, messageStartEvent, eventData),
            FlowzerSignalStartEvent signalStartEvent => StartNewSignalInstance(process, signalStartEvent, eventData),
            StartEvent => StartNewPlainInstance(process, eventData),
            _ => throw new NotSupportedException(
                $"Der Einstieg über den Knotentyp \"{selectedStartNode.GetType().Name}\" wird vom CoreEngine-Vertrag aktuell nicht unterstützt.")
        };
    }

    private InstanceEngine StartNewPlainInstance(Process process, CoreEventData eventData)
    {
        var plainStartNodes = process
            .GetStartFlowNodes()
            .Where(node => node is StartEvent && node is not FlowzerMessageStartEvent && node is not FlowzerSignalStartEvent)
            .ToArray();

        if (plainStartNodes.Length != 1 ||
            !string.Equals(plainStartNodes[0].Id, eventData.BpmnNodeId, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "CoreEngine unterstützt aktuell nur Definitionen mit genau einem expliziten Plain-Start-Event.");
        }

        var processEngine = new ProcessEngine(process, _flowzerConfig);
        var instanceEngine = processEngine.StartProcess(ToVariables(eventData.AdditionalData));
        instanceEngine.InstanceId = eventData.InstanceId;

        return instanceEngine;
    }

    private InstanceEngine StartNewMessageInstance(
        Process process,
        FlowzerMessageStartEvent messageStartEvent,
        CoreEventData eventData)
    {
        var processEngine = new ProcessEngine(process, _flowzerConfig);
        var instanceEngine = processEngine.HandleMessage(new Message
        {
            Name = messageStartEvent.MessageDefinition.Name,
            Variables = SerializeAdditionalData(eventData.AdditionalData),
            InstanceId = eventData.InstanceId
        });
        instanceEngine.InstanceId = eventData.InstanceId;

        return instanceEngine;
    }

    private InstanceEngine StartNewSignalInstance(
        Process process,
        FlowzerSignalStartEvent signalStartEvent,
        CoreEventData eventData)
    {
        var instanceEngine = CreateInstanceEngine(process);
        instanceEngine.HandleSignal(
            signalStartEvent.Signal.Name,
            SerializeAdditionalData(eventData.AdditionalData),
            signalStartEvent);
        instanceEngine.InstanceId = eventData.InstanceId;

        return instanceEngine;
    }

    private static void CompleteInteraction(InstanceEngine instanceEngine, CoreEventData eventData)
    {
        var token = ResolveActiveInteraction(instanceEngine, eventData);

        if (token == null)
        {
            throw new InvalidOperationException(
                $"Für die Instanz {eventData.InstanceId} wartet keine aktive Interaktion auf dem Knoten \"{eventData.BpmnNodeId}\".");
        }

        instanceEngine.HandleTaskResult(token.Id, ToVariables(eventData.AdditionalData));
    }

    private static Token? ResolveActiveInteraction(InstanceEngine instanceEngine, CoreEventData eventData)
    {
        if (eventData.InteractionId is Guid interactionId)
        {
            var tokenById = instanceEngine
                .GetActiveTasks()
                .SingleOrDefault(activeToken => activeToken.Id == interactionId);

            if (tokenById == null)
            {
                throw new InvalidOperationException(
                    $"Für die Instanz {eventData.InstanceId} wurde keine aktive Interaktion mit der ID \"{interactionId}\" gefunden.");
            }

            if (!string.Equals(tokenById.CurrentFlowNode?.Id, eventData.BpmnNodeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Die Interaktion \"{interactionId}\" gehört nicht zum BPMN-Knoten \"{eventData.BpmnNodeId}\".");
            }

            return tokenById;
        }

        var tokensByNode = instanceEngine
            .GetActiveTasks()
            .Where(activeToken =>
                string.Equals(activeToken.CurrentFlowNode?.Id, eventData.BpmnNodeId, StringComparison.Ordinal))
            .ToArray();

        return tokensByNode.Length switch
        {
            0 => null,
            1 => tokensByNode[0],
            _ => throw new NotSupportedException(
                $"Für den BPMN-Knoten \"{eventData.BpmnNodeId}\" existieren mehrere aktive Interaktionen. Bitte die eindeutige InteractionId verwenden.")
        };
    }

    private static Variables? ToVariables(IReadOnlyDictionary<string, object?> additionalData)
    {
        if (additionalData.Count == 0)
        {
            return null;
        }

        var variables = new Variables();
        foreach (var entry in additionalData)
        {
            variables.SetValue(entry.Key, entry.Value);
        }

        return variables;
    }

    private static string SerializeAdditionalData(IReadOnlyDictionary<string, object?> additionalData)
    {
        return additionalData.Count == 0
            ? "{}"
            : JsonConvert.SerializeObject(additionalData);
    }

    private InstanceEngine CreateInstanceEngine(Process process)
    {
        var masterToken = new Token
        {
            CurrentBaseElement = process,
            State = FlowNodeState.Active,
            Variables = new Variables(),
            ParentTokenId = null,
            ActiveBoundaryEvents = [],
            ProcessInstanceId = Guid.NewGuid()
        };

        return new InstanceEngine([masterToken], _flowzerConfig);
    }

    private CoreInstance CreateInstanceSnapshot(Guid instanceId, InstanceEngine instanceEngine)
    {
        return new CoreInstance
        {
            Id = instanceId,
            State = instanceEngine.State,
            Interactions = CollectInteractions(instanceEngine)
        };
    }

    private static IReadOnlyList<CoreInteraction> CollectInteractions(InstanceEngine instanceEngine)
    {
        var interactions = new List<CoreInteraction>();

        interactions.AddRange(instanceEngine.GetActiveUserTasks().Select(token => new CoreInteraction
        {
            InteractionId = token.Id,
            Type = CoreInteractionType.UserTask,
            NodeId = token.CurrentFlowNode!.Id,
            Name = token.CurrentFlowNode.Name ?? token.CurrentFlowNode.Id,
            Implementation = ((UserTask)token.CurrentFlowNode).Implementation
        }));

        interactions.AddRange(instanceEngine.GetActiveServiceTasks().Select(token => new CoreInteraction
        {
            InteractionId = token.Id,
            Type = CoreInteractionType.ServiceTask,
            NodeId = token.CurrentFlowNode!.Id,
            Name = token.CurrentFlowNode.Name ?? token.CurrentFlowNode.Id,
            Implementation = ((ServiceTask)token.CurrentFlowNode).Implementation
        }));

        return interactions;
    }

    private static CoreSubscription MapInitialSubscription(FlowNode flowNode)
    {
        return flowNode switch
        {
            FlowzerMessageStartEvent messageStartEvent => new CoreSubscription
            {
                Type = CoreSubscriptionType.Message,
                BpmnNodeId = messageStartEvent.Id,
                Name = messageStartEvent.MessageDefinition.Name
            },
            FlowzerSignalStartEvent signalStartEvent => new CoreSubscription
            {
                Type = CoreSubscriptionType.Signal,
                BpmnNodeId = signalStartEvent.Id,
                Name = signalStartEvent.Signal.Name
            },
            _ => new CoreSubscription
            {
                Type = CoreSubscriptionType.Start,
                BpmnNodeId = flowNode.Id,
                Name = flowNode.Name ?? flowNode.Id
            }
        };
    }

    private Process GetLoadedProcess()
    {
        return _process ?? throw new InvalidOperationException("Es wurde noch keine BPMN-Definition geladen.");
    }
}
