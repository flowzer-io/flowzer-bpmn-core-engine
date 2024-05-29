using BPMN.Common;
using BPMN.Events;
using BPMN.Flowzer.Events;
using BPMN.Process;
using Model;
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

    public ProcessInstance HandleMessage(Message message)
    {
        var startEvent = Process.FlowElements
            .OfType<FlowzerMessageStartEvent>()
            .First(e => e.MessageDefinition.Name == message.Name);
        var processInstance = new ProcessInstance
        {
            ProcessModel = Process
        };
        
        Variables? data = JsonConvert.DeserializeObject<Variables>(message.Variables ?? "{}");
        if (data is null)
            data = new Variables();
        
        var token = new Token
        {
            CurrentFlowNode = startEvent,
            InputData = data,
            ProcessInstance = processInstance
        };
        token.OutputData = token.InputData; //TODO @christian: korrekt?
        
        processInstance.Tokens.Add(token);
        var instance = new InstanceEngine(processInstance);
        instance.Run();
        
        return processInstance;
    }

    public Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
    }
}