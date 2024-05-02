using BPMN_Model.Common;
using BPMN_Model.Process;

namespace core_engine;

public class ProcessEngine : ICatchHandler
{
    public DateTime DeployedAt { get; init; }
    public bool IsActive { get; set; }
    public required Process Process { get; init; }
    
    Task<IInstanceEngine> StartProcess(object? data = null)
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

    public Task<IInstanceEngine> HandleTime(DateTime time)
    {
        throw new NotImplementedException();
    }

    public Task<IInstanceEngine> HandleMessage(string messageName, string? correlationKey = null, object? messageData = null)
    {
        throw new NotImplementedException();
    }

    public Task<IInstanceEngine> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
    }
}