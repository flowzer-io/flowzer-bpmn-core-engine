using BPMN.Foundation;

namespace Model;

public class Token
{
    public Guid Id { get; init; } = Guid.NewGuid();
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

    // Der Setter ist nötig, damit der Startzeitpunkt beim Laden aus der Ablage erhalten
    // bleibt: Newtonsoft.Json überspringt schreibgeschützte Auto-Properties, wodurch
    // jeder Ladevorgang den Zeitstempel auf "jetzt" zurücksetzen würde.
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    // Der State-Setter schreibt LastStateChangeTime auf "jetzt". Beim Laden aus der Ablage muss
    // der persistierte Zeitstempel deshalb nach State gesetzt werden; die feste Reihenfolge macht
    // das unabhaengig von der Deklarationsreihenfolge und vom Erzeuger der JSON-Datei.
    [Newtonsoft.Json.JsonProperty(Order = 100)]
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