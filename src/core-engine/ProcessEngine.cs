using BPMN.Events;
using BPMN.Flowzer.Events;
using BPMN.Process;
using Model;

namespace core_engine;

public class ProcessEngine(Process process)
{
    public Process Process { get; set; } = process;

    public InstanceEngine StartProcess(Variables? data = null)
    {
        var instance = new InstanceEngine(new ProcessInstance { ProcessModel = Process });
        instance.Start(data);
        return instance;
    }

    public IEnumerable<TimerEventDefinition> ActiveTimers()
    {
        return Process.FlowElements
            .OfType<FlowzerTimerStartEvent>()
            .Select(e => e.TimerDefinition);
    }

    public IEnumerable<MessageDefinition> GetActiveCatchMessages()
    {
        return Process.FlowElements
            .OfType<FlowzerMessageStartEvent>()
            .Select(e => new MessageDefinition { Name = e.Message.Name });
    }

    public IEnumerable<SignalDefinition> GetActiveCatchSignals()
    {
        return Process.FlowElements
            .OfType<FlowzerSignalStartEvent>()
            .Select(e => new SignalDefinition
            {
                Id = e.Signal.FlowzerId ?? "", 
                Name = e.Signal.Name
            });
    }

    public Task<ProcessInstance> HandleTime(DateTime time)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleMessage(string messageName, string? correlationKey = null,
        object? messageData = null)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
    }
}