using BPMN.Events;
using BPMN.Process;
using FlowzerBPMN;

namespace core_engine;

public class ProcessDefinition : ICatchHandler
{
    public DateTime DeployedAt { get; init; }
    public bool IsActive { get; set; }
    public required Process Process { get; init; }
    
    public ProcessInstance StartProcess(Variables? data = null)
    {
        var instance = new ProcessInstance()
        {
            ProcessModel = Process
        };
        instance.Start(data);
        return instance;
    }
    
    public List<TimerEventDefinition> GetActiveTimers()
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

    public Task<ProcessInstance> HandleTime(DateTime time)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleMessage(string messageName, string? correlationKey = null, object? messageData = null)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
    }
}