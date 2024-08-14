using core_engine.Exceptions;
using core_engine.Extensions;
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
        if (Tokens.Count == 1) // Da es noch keine Tokens gibt, muss es ein StartNode sein  // ToDo: Das geht nicht mehr gut mit der neuen Tokenlogik
        {
            var startFlowNodes =
                ((Process)MasterToken.CurrentBaseElement)
                    .GetStartFlowNodes()
                    .OfType<FlowzerSignalStartEvent>()
                    .ToArray();

            var startFlowNode = startFlowNodes.Length == 1
                ? startFlowNodes.Single()
                : startFlowNodes.Single(n => n == startNode);

            Tokens.Add(new Token
            {
                CurrentBaseElement = startFlowNode,
                ActiveBoundaryEvents = [],
                OutputData = JsonConvert.DeserializeObject<Variables>(signalData ?? "{}") ?? new Variables(),
                ParentTokenId = MasterToken.Id,
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