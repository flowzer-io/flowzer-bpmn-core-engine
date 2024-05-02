using BPMN_Model.Common;

namespace core_engine;

public interface ICatchHandler
{
    List<TimerDefinition> GetActiveTimers();
    List<MessageDefinition> GetActiveCatchMessages();
    List<SignalDefinition> GetActiveCatchSignals();
    
    Task<InstantiatedProcess> HandleTime(DateTime time);
    Task<InstantiatedProcess> HandleMessage(string messageName, string? correlationKey = null, object? messageData = null);
    Task<InstantiatedProcess> HandleSignal(string signalName, object? signalData = null);
}