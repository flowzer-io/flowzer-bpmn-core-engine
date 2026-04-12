using core_engine.Exceptions;
using Newtonsoft.Json;

namespace core_engine;

public class ProcessEngine : ICatchHandler
{
    private readonly Dictionary<string, TimerStartState> _timerStartStates;
    private readonly DateTime _timerReferenceTime = DateTime.UtcNow;

    public ProcessEngine(Process process, FlowzerConfig? flowzerConfig = null)
    {
        Process = process;
        FlowzerConfig = flowzerConfig ?? FlowzerConfig.Default;
        _timerStartStates = Process.FlowElements
            .OfType<FlowzerTimerStartEvent>()
            .Select(startEvent => TimerStartState.Create(startEvent, _timerReferenceTime))
            .Where(state => state.HasPendingOccurrence)
            .ToDictionary(state => state.StartEvent.Id, StringComparer.Ordinal);
    }

    public Process Process { get; set; }
    public FlowzerConfig FlowzerConfig { get; }

    public InstanceEngine StartProcess(Variables? data = null)
    {
        var instance = CreateInstanceEngine();
        instance.Start(data);
        return instance;
    }

    public InstanceEngine StartProcessByTimerStartEvent(string flowNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowNodeId);

        if (!_timerStartStates.TryGetValue(flowNodeId, out var timerStartState))
        {
            throw new ArgumentException(
                $"No timer start event with the id \"{flowNodeId}\" exists in process \"{Process.Id}\".",
                nameof(flowNodeId));
        }

        var instance = CreateInstanceEngine();
        instance.HandleTime(DateTime.UtcNow, timerStartState.StartEvent);

        AdvanceOrRemoveTimerStartState(timerStartState);

        return instance;
    }

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
    /// Führt fällige Timer-Start-Events aus und plant wiederkehrende Start-Timer auf Basis derselben Definition neu ein.
    /// </summary>
    public InstanceEngine[] HandleTime(DateTime time)
    {
        var dueStates = _timerStartStates.Values
            .Where(state => state.DueAt <= time)
            .OrderBy(state => state.DueAt)
            .ToArray();

        var instances = new List<InstanceEngine>();

        foreach (var dueState in dueStates)
        {
            var currentState = dueState;
            while (currentState.DueAt <= time)
            {
                var instanceEngine = CreateInstanceEngine();
                instanceEngine.HandleTime(time, currentState.StartEvent);
                instances.Add(instanceEngine);

                if (!TryAdvanceTimerStartState(currentState, out currentState))
                {
                    _timerStartStates.Remove(dueState.StartEvent.Id);
                    break;
                }

                _timerStartStates[dueState.StartEvent.Id] = currentState;
            }
        }

        return instances.ToArray();
    }

    public List<DateTime> ActiveTimers => _timerStartStates.Values
        .Select(state => state.DueAt)
        .ToList();

    public List<TimerSubscriptionDescriptor> ActiveTimerSubscriptions => _timerStartStates.Values
        .Select(state => new TimerSubscriptionDescriptor(
            state.DueAt,
            state.StartEvent.Id,
            TimerSubscriptionKind.ProcessStartEvent,
            RemainingOccurrences: state.RemainingOccurrences))
        .ToList();

    public List<MessageDefinition> ActiveCatchMessages
    {
        get
        {
            return Process.FlowElements
                .OfType<FlowzerMessageStartEvent>()
                .Select(e => new MessageDefinition { Name = e.MessageDefinition.Name })
                .ToList();
        }
    }

    public List<string> ActiveCatchSignals
    {
        get
        {
            return Process.FlowElements
                .OfType<FlowzerSignalStartEvent>()
                .Select(e => e.Signal.Name)
                .ToList();
        }
    }

    public List<Token> ActiveUserTasks()
    {
        //TODO: implement to support userasks as initialisation of a instancde
        return [];
    }

    private static bool TryAdvanceTimerStartState(TimerStartState currentState, out TimerStartState nextState)
    {
        var currentSchedule = new TimerSchedule(
            currentState.DueAt,
            currentState.RepeatInterval,
            currentState.RemainingOccurrences);

        if (!TimerScheduleCalculator.TryAdvanceSchedule(currentSchedule, out var nextSchedule))
        {
            nextState = default;
            return false;
        }

        nextState = currentState with
        {
            DueAt = nextSchedule.DueAt,
            RemainingOccurrences = nextSchedule.RemainingOccurrences
        };
        return true;
    }

    private void AdvanceOrRemoveTimerStartState(TimerStartState timerStartState)
    {
        if (TryAdvanceTimerStartState(timerStartState, out var nextState))
        {
            _timerStartStates[timerStartState.StartEvent.Id] = nextState;
            return;
        }

        _timerStartStates.Remove(timerStartState.StartEvent.Id);
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

    private readonly record struct TimerStartState(
        FlowzerTimerStartEvent StartEvent,
        DateTime DueAt,
        TimeSpan? RepeatInterval,
        int? RemainingOccurrences)
    {
        public bool HasPendingOccurrence => RemainingOccurrences == null || RemainingOccurrences > 0;

        public static TimerStartState Create(FlowzerTimerStartEvent startEvent, DateTime referenceTime)
        {
            var schedule = TimerScheduleCalculator.CreateInitialSchedule(
                referenceTime,
                startEvent.TimerDefinition,
                startEvent);

            return new TimerStartState(
                startEvent,
                schedule.DueAt,
                schedule.RepeatInterval,
                schedule.RemainingOccurrences);
        }
    }
}
