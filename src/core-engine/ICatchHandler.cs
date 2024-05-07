using BPMN.Events;
using FlowzerBPMN;

namespace core_engine;

public interface ICatchHandler
{
    List<TimerEventDefinition> GetActiveTimers();
    List<MessageDefinition> GetActiveCatchMessages();
    List<SignalDefinition> GetActiveCatchSignals();
    
    Task<ProcessInstance> HandleTime(DateTime time);
    Task<ProcessInstance> HandleMessage(string messageName, string? correlationKey = null, object? messageData = null);
    Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null);
}