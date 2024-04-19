using Activities;
using Common;
using Data;

namespace Events;

public class EscalationEventDefinition
{
    public Escalation? EscalationRed { get; set; }
}

public class Escalation
{
    public string EscalationCode { get; set; } = "";
    public string Name { get; set; } = "";
    
    public ItemDefinition? StructureRef { get; set; }
}

public abstract class Event : FlowNode
{
    public List<Escalation> Escalations { get; set; } = [];
}

public abstract class ThrowEvent : Event
{
    public InputSet InputSet { get; set; }
    public List<DataInput> DataInputs { get; set; } = [];
    public List<DataInputAssociation> DataInputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}

public class IntermediateThrowEvent : ThrowEvent;
public class EndEvent : ThrowEvent;
public class ImplicitThrowEvent : ThrowEvent;
public class EventDefinition;

public abstract class CatchEvent : Event
{
    public OutputSet OutputSet { get; set; }
    public List<DataOutput> DataOutputs { get; set; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}

public class StartEvent : CatchEvent;
public class IntermediateCatchEvent : CatchEvent;

public class BoundaryEvent(Activity attachedToRef) : CatchEvent
{
    public bool CancelActivity { get; set; }
    public Activity AttachedToRef { get; set; } = attachedToRef;
}
