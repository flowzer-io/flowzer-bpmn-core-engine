using BPMN_Model.Common;

namespace core_engine;

public interface ICatchHandler
{
    List<TimerDefinition> GetActiveTimers();
    List<MessageDefinition> GetActiveCatchMessages();
    List<SignalDefinition> GetActiveCatchSignals();
    
    Task<IInstanceEngine> HandleTime(DateTime time);
    Task<IInstanceEngine> HandleMessage(string messageName, string? correlationKey = null, object? messageData = null);
    Task<IInstanceEngine> HandleSignal(string signalName, object? signalData = null);
}