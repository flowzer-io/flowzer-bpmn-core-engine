using Newtonsoft.Json;

namespace core_engine;

public partial class InstanceEngine
{
    /// <summary>
    /// Gibt eine Liste aktuell erwarteter Nachrichten zurück
    /// </summary>
    /// <returns>Liste von Nachrichten inkl. CorrelationKey</returns>
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
        }

        eventToken = ActiveTokens.FirstOrDefault(t => t.ActiveBoundaryEvents.Any(b =>
            b is FlowzerBoundaryMessageEvent boundaryEvent &&
            boundaryEvent.MessageDefinition.Name == message.Name &&
            boundaryEvent.MessageDefinition.FlowzerCorrelationKey == message.CorrelationKey));

        if (eventToken is not null) // Wenn es ein BoundaryEvent gibt, das auf die Nachricht wartet
        {
            var boundaryEvent = eventToken.ActiveBoundaryEvents.First(b =>
                b is FlowzerBoundaryMessageEvent boundaryEvent &&
                boundaryEvent.MessageDefinition.Name == message.Name &&
                boundaryEvent.MessageDefinition.FlowzerCorrelationKey == message.CorrelationKey);
            if (boundaryEvent.CancelActivity)
            {
                eventToken.State = FlowNodeState.Withdrawn;
            }

            Instance.Tokens.Add(new Token
            {
                CurrentFlowNode = boundaryEvent,
                ActiveBoundaryEvents = [],
                OutputData = data,
                ProcessInstance = Instance,
                State = FlowNodeState.Completing
            });
        }
        
        Run();
    }
}