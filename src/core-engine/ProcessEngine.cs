using core_engine.Exceptions;
using Newtonsoft.Json;

namespace core_engine;

public class ProcessEngine(Process process, FlowzerConfig? flowzerConfig = null) : ICatchHandler
{
    private readonly DateTime _timerReferenceTime = DateTime.UtcNow;
    private readonly HashSet<string> _triggeredTimerStartEventIds = [];
    
    public Process Process { get; set; } = process;
    public FlowzerConfig FlowzerConfig { get; } = flowzerConfig ?? FlowzerConfig.Default;

    public InstanceEngine StartProcess(Variables? data = null)
    {
        var instance = CreateInstanceEngine();
        instance.Start(data);
        return instance;
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
        var instanceEngine = CreateInstanceEngine();

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
            var instanceEngine = CreateInstanceEngine();

            instanceEngine.HandleSignal(signalName, JsonConvert.SerializeObject(signalData), startEvent);

            return instanceEngine;
        }).ToArray();
    }

    /// <summary>
    /// Führt fällige Timer-Start-Events einmalig aus und startet dafür neue Instanzen.
    /// Die Persistenz bzw. echte Wiederholungslogik bleibt weiterhin ein separates Folgethema.
    /// </summary>
    public InstanceEngine[] HandleTime(DateTime time)
    {
        var dueStartEvents = GetPendingTimerStartEvents()
            .Where(startEvent => GetStartTimerDueDate(startEvent) <= time)
            .ToArray();

        foreach (var startEvent in dueStartEvents)
        {
            _triggeredTimerStartEventIds.Add(startEvent.Id);
        }

        return dueStartEvents.Select(startEvent =>
        {
            var instanceEngine = CreateInstanceEngine();
            instanceEngine.HandleTime(time, startEvent);
            return instanceEngine;
        }).ToArray();
    }

    public List<DateTime> ActiveTimers
    {
        get
        {
            return GetPendingTimerStartEvents()
                .Select(GetStartTimerDueDate)
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

    public List<Token> ActiveUserTasks()
    {
        //TODO: implement to support userasks as initialisation of a instancde
        return new List<Token>();
    }

    private IEnumerable<FlowzerTimerStartEvent> GetPendingTimerStartEvents()
    {
        return Process.FlowElements
            .OfType<FlowzerTimerStartEvent>()
            .Where(startEvent => !_triggeredTimerStartEventIds.Contains(startEvent.Id));
    }

    private DateTime GetStartTimerDueDate(FlowzerTimerStartEvent startEvent)
    {
        return TimerDueDateCalculator.GetDueDate(_timerReferenceTime, startEvent.TimerDefinition, startEvent);
    }

    private InstanceEngine CreateInstanceEngine()
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

        return new InstanceEngine([masterToken], FlowzerConfig);
    }
}
