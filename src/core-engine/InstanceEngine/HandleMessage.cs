using core_engine.Exceptions;
using core_engine.Extensions;
using Newtonsoft.Json;

namespace core_engine;

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

        if (Tokens.Count == 0)
        {
            messageDefinitions.AddRange(
                ((Process)MasterToken.CurrentBaseElement)
                .GetStartFlowNodes()
                .OfType<FlowzerMessageStartEvent>()
                .Select(startEvent => new MessageDefinition { Name = startEvent.MessageDefinition.Name }));
        }

        return messageDefinitions;
    }

    public void HandleMessage(Message message)
    {
        var data = JsonConvert.DeserializeObject<Variables>(message.Variables ?? "{}") ?? new Variables();

        if (Tokens.Count == 1) // Wenn es nur ein Token gibt, muss es ein Startevent sein
        {
            var startEvent = ((Process)MasterToken.CurrentBaseElement).GetStartFlowNodes().OfType<FlowzerMessageStartEvent>()
                .First(e => e.MessageDefinition.Name == message.Name);

            Tokens.Add(new Token
            {
                CurrentBaseElement = startEvent,
                ActiveBoundaryEvents = [],
                OutputData = data,
                State = FlowNodeState.Completing,
                ParentTokenId = MasterToken.Id,
                ProcessInstanceId = MasterToken.Id,
            });
            Run();
            return;
        }

        var eventToken = ActiveTokens.FirstOrDefault(t =>
            t.CurrentBaseElement is FlowzerIntermediateMessageCatchEvent messageCatchEvent &&
            messageCatchEvent.MessageDefinition.Name == message.Name &&
            messageCatchEvent.MessageDefinition.FlowzerCorrelationKey == message.CorrelationKey ||
            t.CurrentBaseElement is ReceiveTask receiveTask &&
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

            Tokens.Add(new Token
            {
                CurrentBaseElement = boundaryEvent,
                ActiveBoundaryEvents = [],
                OutputData = data,
                State = FlowNodeState.Completing,
                ParentTokenId = activeToken.ParentTokenId,
                ProcessInstanceId = activeToken.ProcessInstanceId,
            });
            Run();
            return;
        }

        throw new FlowzerRuntimeException($"Es wurde keine passende Nachricht für {message.Name} gefunden.");
    }
}