using BPMN_Model.Common;
using BPMN_Model.Process;

namespace core_engine;



public class NotInstantiatedProcess : ICatchHandler
{
    public DateTime DeployedAt { get; init; }
    public bool IsActive { get; set; }
    public required Process Process { get; init; }
    
    Task<InstantiatedProcess> StartProcess(object? data = null)
    {
        throw new NotImplementedException();
    }
    
    public List<TimerDefinition> GetActiveTimers()
    {
        throw new NotImplementedException();
    }

    public List<MessageDefinition> GetActiveCatchMessages()
    {
        throw new NotImplementedException();
    }

    public List<SignalDefinition> GetActiveCatchSignals()
    {
        throw new NotImplementedException();
    }

    public Task<InstantiatedProcess> HandleTime(DateTime time)
    {
        throw new NotImplementedException();
    }

    public Task<InstantiatedProcess> HandleMessage(string messageName, string? correlationKey = null, object? messageData = null)
    {
        throw new NotImplementedException();
    }

    public Task<InstantiatedProcess> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
    }
}