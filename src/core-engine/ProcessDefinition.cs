using BPMN.Process;
using FlowzerBPMN;

namespace core_engine;

public class ProcessDefinition : ICatchHandler
{
    public DateTime DeployedAt { get; init; }
    public bool IsActive { get; set; }
    public required Process Process { get; init; }
    
    public ProcessInstance StartProcess(object? data = null)
    {
        var instance = new ProcessInstance
        {
            ProcessModel = Process
        };
        
        foreach (var processStartFlowNode in Process.StartFlowNodes)
        {
            instance.Tokens.Add(new Token
            {
                CurrentFlowNode = processStartFlowNode
            });
        }

        return instance;
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