using core_engine.Exceptions;
using Newtonsoft.Json;

namespace core_engine;

public class ProcessEngine(Process process) : ICatchHandler
{
    
    public Process Process { get; set; } = process;

    public InstanceEngine StartProcess(Variables? data = null)
    {
        var masterToken = new Token
        {
            CurrentBaseElement = Process,
            State = FlowNodeState.Active,
            Variables = new Variables(),
            ParentTokenId = null,
            ActiveBoundaryEvents = [],
            ProcessInstanceId = Guid.NewGuid(),
        };
        var instance = new InstanceEngine([masterToken]);
        instance.Start(data);
        return instance;
    }

   
    private DateTime GetDateFromTimerDefinition(DateTime referenceTime, TimerEventDefinition timerDefinition, FlowNode flowNode)
    {
        if (timerDefinition.TimeCycle != null)
        {
            var timeSpan = ISO8601Date.DateExtensions.ParseIso8601Duration(timerDefinition.TimeCycle.Body);
            return referenceTime.Add(timeSpan);
        }
        else if (timerDefinition.TimeDate != null)
        {
            return DateTime.Parse(timerDefinition.TimeDate.Body);
        }

        throw new ModelValidationException("Timer definition is invalid. for node " + flowNode.Name);
    }


    // public async Task<InstanceEngine> HandleTime(DateTime time)
    // {
    //     var processInstance = new ProcessInstance
    //     {
    //         ProcessModel = Process
    //     };
    //     return  new InstanceEngine(processInstance);
    // }

    public InstanceEngine HandleMessage(Message message)
    {
        var masterToken = new Token
        {
            CurrentBaseElement = Process,
            State = FlowNodeState.Active,
            Variables = new Variables(),
            ParentTokenId = null,
            ActiveBoundaryEvents = [],
            ProcessInstanceId = Guid.NewGuid(),
        };
        var instanceEngine = new InstanceEngine([masterToken]);

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
            var masterToken = new Token
            {
                CurrentBaseElement = Process,
                State = FlowNodeState.Active,
                Variables = new Variables(),
                ParentTokenId = null,
                ActiveBoundaryEvents = [],
                ProcessInstanceId = Guid.NewGuid(),
            };
            var instanceEngine = new InstanceEngine([masterToken]);

            instanceEngine.HandleSignal(signalName, JsonConvert.SerializeObject(signalData), startEvent);

            return instanceEngine;
        }).ToArray();
    }

    public List<DateTime> ActiveTimers
    {
        get
        {
            return Process.FlowElements
                .OfType<FlowzerTimerStartEvent>()
                .Select(e => GetDateFromTimerDefinition(DateTime.Now, e.TimerDefinition, e))
                .ToList();
        }
    }

    public List<MessageDefinition> ActiveCatchMessages
    {
        get
        {
            return Process.FlowElements
                .OfType<FlowzerMessageStartEvent>()
                .Select(e => new MessageDefinition() { Name = e.MessageDefinition.Name })
                .ToList();
        }
    }

    public List<string> ActiveCatchSignals
    {
        get
        {
            return Process.FlowElements
                .OfType<FlowzerSignalStartEvent>()
                .Select(e => e.Signal.Name).ToList();
        }
    }
}