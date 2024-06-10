using core_engine.Exceptions;
using Newtonsoft.Json;

namespace core_engine;

public partial class InstanceEngine
{
    public IEnumerable<string> GetActiveCatchSignals()
    {
        List<string> signalDefinitions = [];

        foreach (var token in ActiveTokens)
        {
            if (token.CurrentFlowNode is FlowzerIntermediateSignalCatchEvent catchEvent)
                signalDefinitions.Add(catchEvent.Signal.Name);

            signalDefinitions.AddRange(token.ActiveBoundaryEvents
                .OfType<FlowzerBoundarySignalEvent>()
                .Select(e => e.Signal.Name));
        }

        return signalDefinitions.Distinct();
    }

    public void HandleSignal(string signalName, string? signalData = null, FlowNode? startNode = null)
    {
        if (Instance.Tokens.Count == 0) // Da es noch keine Tokens gibt, muss es ein StartNode sein
        {
            var startFlowNodes =
                Instance.ProcessModel
                    .StartFlowNodes
                    .OfType<FlowzerSignalStartEvent>()
                    .ToArray();

            var startFlowNode = startFlowNodes.Length == 1
                ? startFlowNodes.Single()
                : startFlowNodes.Single(n => n == startNode);

            Instance.Tokens.Add(new Token
            {
                CurrentFlowNode = startFlowNode,
                ActiveBoundaryEvents = [],
                OutputData = JsonConvert.DeserializeObject<Variables>(signalData ?? "{}") ?? new Variables(),
                ProcessInstance = Instance,
                State = FlowNodeState.Completing
            });
            Run();
            return;
        }

        foreach (var token in ActiveTokens)
        {
            if (token.CurrentFlowNode is FlowzerIntermediateSignalCatchEvent catchEvent &&
                catchEvent.Signal.Name == signalName)
            {
                token.OutputData = JsonConvert.DeserializeObject<Variables>(signalData ?? "{}") ?? new Variables();
                token.State = FlowNodeState.Completing;
                Run();
                return;
            }

            var boundaryEvent = token.ActiveBoundaryEvents
                .OfType<FlowzerBoundarySignalEvent>()
                .SingleOrDefault(e => e.Signal.Name == signalName);

            if (boundaryEvent == null) continue;
            token.OutputData = JsonConvert.DeserializeObject<Variables>(signalData ?? "{}") ?? new Variables();
            token.State = FlowNodeState.Completing;
            Run();
            return;
        }

        throw new FlowzerRuntimeException("No signal catch event found");
    }
}