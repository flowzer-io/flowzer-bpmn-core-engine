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
        var startEvent = Process.FlowElements
            .OfType<FlowzerMessageStartEvent>()
            .First(e => e.MessageDefinition.Name == message.Name);
        var processInstance = new ProcessInstance
        {
            ProcessModel = Process
        };
        
        var data = JsonConvert.DeserializeObject<Variables>(message.Variables ?? "{}") ?? new Variables();

        var token = new Token
        {
            CurrentFlowNode = startEvent,
            ActiveBoundaryEvents = processInstance.ProcessModel
                .FlowElements
                .OfType<BoundaryEvent>()
                .Where(b => b.AttachedToRef == startEvent)
                .Select(b => b.ApplyResolveExpression<BoundaryEvent>(FlowzerConfig.Default.ExpressionHandler.ResolveString, processInstance.ProcessVariables)).ToList(),
            InputData = data,
            ProcessInstance = processInstance
        };
        token.OutputData = token.InputData; //TODO @christian: korrekt?
        
        processInstance.Tokens.Add(token);
        var instance = new InstanceEngine(processInstance);
        instance.Run();
        
        return instance;
    }

    public Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
    }
}