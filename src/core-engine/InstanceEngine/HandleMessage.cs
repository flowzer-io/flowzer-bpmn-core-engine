using core_engine.Exceptions;
using Newtonsoft.Json;

namespace core_engine.InstanceEngine;

public partial class InstanceEngine
{
    /// <summary>
    /// Gibt eine Liste aktuell erwarteter Nachrichten zurück
    /// </summary>
    /// <returns>Liste von Nachrichten inklusive CorrelationKey</returns>
    /// <exception cref="NotImplementedException"></exception>
    public List<MessageDefinition> GetActiveCatchMessages()
    {
        var messageDefinitions = new List<MessageDefinition>();
        foreach (var token in ActiveTokens)
        {
            switch (token.CurrentFlowNode)
            {
                case FlowzerIntermediateMessageCatchEvent catchEvent:
                    messageDefinitions.Add(new MessageDefinition()
                    {
                        Name = catchEvent.MessageDefinition.Name,
                        FlowzerCorrelationKey = catchEvent.MessageDefinition.FlowzerCorrelationKey
                    });
                    break;

                case ReceiveTask { MessageRef: not null } receiveTask:
                    messageDefinitions.Add(new MessageDefinition()
                    {
                        Name = receiveTask.MessageRef.Name,
                        FlowzerCorrelationKey = receiveTask.MessageRef.FlowzerCorrelationKey
                    });
                    break;
            }

            messageDefinitions.AddRange(token.ActiveBoundaryEvents.OfType<FlowzerBoundaryMessageEvent>()
                .Select(boundaryEvent => new MessageDefinition()
                {
                    Name = boundaryEvent.MessageDefinition.Name,
                    FlowzerCorrelationKey = boundaryEvent.MessageDefinition.FlowzerCorrelationKey
                }));
        }

        if (Instance.Tokens.Count == 0)
        {
            messageDefinitions.AddRange(
                Instance.ProcessModel
                    .StartFlowNodes
                    .OfType<FlowzerMessageStartEvent>()
                    .Select(startEvent => new MessageDefinition() { Name = startEvent.MessageDefinition.Name }));
        }

        return messageDefinitions;
    }

    public void HandleMessage(Message message)
    {
        var data = JsonConvert.DeserializeObject<Variables>(message.Variables ?? "{}") ?? new Variables();

        if (Instance.Tokens.Count == 0) // Wenn es noch keine Tokens gibt, muss es ein StartEvent sein.
        {
            var startEvent = Instance.ProcessModel.StartFlowNodes.OfType<FlowzerMessageStartEvent>()
                .First(e => e.MessageDefinition.Name == message.Name);
            Instance.Tokens.Add(new Token
            {
                CurrentFlowNode = startEvent,
                ActiveBoundaryEvents = [],
                OutputData = data,
                ProcessInstance = Instance,
                State = FlowNodeState.Completing
            });
            Run();
            return;
        }
        
        var eventToken = ActiveTokens.FirstOrDefault(t =>
            t.CurrentFlowNode is FlowzerIntermediateMessageCatchEvent messageCatchEvent &&
            messageCatchEvent.MessageDefinition.Name == message.Name &&
            messageCatchEvent.MessageDefinition.FlowzerCorrelationKey == message.CorrelationKey ||
            t.CurrentFlowNode is ReceiveTask receiveTask &&
            receiveTask.MessageRef!.Name == message.Name &&
            receiveTask.MessageRef.FlowzerCorrelationKey == message.CorrelationKey);

        if (eventToken is not null) // Wenn es ein IntermediateCatchEvent oder ReceiveToken gibt, das auf die Nachricht wartet
        {
            eventToken.OutputData = data;
            eventToken.State = FlowNodeState.Completing;
            Run();
            return;
        }

        foreach (var activeToken in ActiveTokens)
        {
            var boundaryEvent = activeToken
                .ActiveBoundaryEvents
                .OfType<FlowzerBoundaryMessageEvent>()
                .FirstOrDefault(x => 
                    x.MessageDefinition.Name == message.Name &&
                    x.MessageDefinition.FlowzerCorrelationKey == message.CorrelationKey);
            if (boundaryEvent is null) continue;
            
            if (boundaryEvent.CancelActivity)
            {
                activeToken.State = FlowNodeState.Withdrawn;
            }

            Instance.Tokens.Add(new Token
            {
                CurrentFlowNode = boundaryEvent,
                ActiveBoundaryEvents = [],
                OutputData = data,
                ProcessInstance = Instance,
                State = FlowNodeState.Completing
            });
            Run();
            return;
        }

        throw new FlowzerRuntimeException($"Es wurde keine passende Nachricht für {message.Name} gefunden.");
    }
}