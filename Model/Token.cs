namespace Model;

public class Token : ICatchHandler

{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid ProcessInstanceId { get; init; }
    public virtual ProcessInstance? ProcessInstance { get; init; }
    public required FlowNode CurrentFlowNode { get; init; }
    private FlowNodeState _state = FlowNodeState.Ready;

    public FlowNodeState State
    {
        get => _state;
        set
        {
            _state = value;
            LastStateChangeTime = DateTime.UtcNow;
        }
    }

    public DateTime StartTime { get; } = DateTime.UtcNow;
    public DateTime LastStateChangeTime { get; set; } = DateTime.UtcNow;
    public Token? PreviousToken { get; set; }
    public SequenceFlow? LastSequenceFlow { get; set; }

    public Variables InputData { get; set; } = new();
    public Variables? OutputData { get; set; }
    public List<TimerEventDefinition> ActiveTimers { get; set; } = [];
    public List<MessageDefinition> ActiveCatchMessages { get; set; } = [];
    public List<SignalDefinition> ActiveCatchSignals { get; set; } = [];
}