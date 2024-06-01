namespace Model;

public class Token

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

    public Variables InputData { get; set; } = new Variables();
    public Variables? OutputData { get; set; }
    
    /// <summary>
    /// If this token is destroyed, it will not be used to create new tokens
    /// </summary>
    public bool IsDistroyed { get; set; }
}