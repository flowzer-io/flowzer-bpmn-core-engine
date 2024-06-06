using Newtonsoft.Json;

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
            .Select(e => new MessageDefinition() { Name = e.MessageDefinition.Name });
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

    public InstanceEngine HandleMessage(Message message)
    {
        var processInstance = new ProcessInstance
        {
            ProcessModel = Process
        };
        var instanceEngine = new InstanceEngine(processInstance);

        instanceEngine.HandleMessage(message);
        
        return instanceEngine;
    }

    public InstanceEngine[] HandleSignal(string signalName, object? signalData = null)
    {
        return Process.FlowElements
            .OfType<FlowzerSignalStartEvent>()
            .Where(e => e.Signal.Name == signalName)
            .Select(startEvent =>
        {
            var processInstance = new ProcessInstance
            {
                ProcessModel = Process
            };
            var instanceEngine = new InstanceEngine(processInstance);

            instanceEngine.HandleSignal(signalName, JsonConvert.SerializeObject(signalData), startEvent);

            return instanceEngine;
        }).ToArray();
    }
}