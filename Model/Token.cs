using BPMN.Foundation;

namespace Model;

public class Token
{
    public Guid Id { get; } = Guid.NewGuid();
    public required Guid ProcessInstanceId { get; init; }

    public required IBaseElement CurrentBaseElement { get; init; }
    public FlowNode? CurrentFlowNode => CurrentBaseElement as FlowNode;

    public required List<BoundaryEvent> ActiveBoundaryEvents { get; init; }
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

    public Variables? Variables { get; set; }
    public Variables? OutputData { get; set; }

    public Guid? ParentTokenId { get; init; }

    public override string ToString()
    {
        return $"{CurrentBaseElement.GetType()} " +
               (CurrentBaseElement.GetType().IsAssignableTo(typeof(FlowNode))
                   ? CurrentFlowNode?.Name
                   : CurrentBaseElement.Id)
               + $" ({State} + )";
    }
}