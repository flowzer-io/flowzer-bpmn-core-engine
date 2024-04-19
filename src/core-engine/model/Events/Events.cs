using Activities;
using Common;
using Data;
using Foundation;
using Service;

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
    public List<Property> Properties { get; set; } = [];
}

public abstract class ThrowEvent(InputSet inputSet) : Event
{
    public InputSet InputSet { get; set; } = inputSet;
    public List<DataInput> DataInputs { get; set; } = [];
    public List<DataInputAssociation> DataInputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}

public class IntermediateThrowEvent(InputSet inputSet) : ThrowEvent(inputSet);
public class EndEvent(InputSet inputSet) : ThrowEvent(inputSet);
public class ImplicitThrowEvent(InputSet inputSet) : ThrowEvent(inputSet);
public class EventDefinition : RootElement;
public class TerminateEventDefinition : EventDefinition;

public class LinkEventDefinition : EventDefinition
{
    public string Name { get; set; } = "";
    public List<LinkEventDefinition> Sources { get; set; } = [];
    public LinkEventDefinition? Target { get; set; }
}

public class ErrorEventDefinition : EventDefinition
{
    public Error? Error { get; set; }
}

public class CancelEventDefinition : EventDefinition;

public class MessageEventDefinition : EventDefinition
{
    public Operation? OperationRef { get; set; }
    public Message? MessageRef { get; set; }
}

public class CompensateEventDefinition : EventDefinition
{
    public bool WaitForCompletion { get; set; }
    public Activity? ActivityRef { get; set; }
}

public class SignalEventDefinition : EventDefinition
{
    public Signal? SignalRef { get; set; }
}

public class TimerEventDefinition : EventDefinition
{
    public Expression? TimeDate { get; set; }
    public Expression? TimeCycle { get; set; }
    public Expression? TimeDuration { get; set; }
}

public class ConditionalEventDefinition : EventDefinition
{
    public Expression? Condition { get; set; }
}

public class Signal
{
    public string Name { get; set; } = "";
    public ItemDefinition? StructureRef { get; set; }
}

public abstract class CatchEvent(OutputSet outputSet) : Event
{
    public OutputSet OutputSet { get; set; } = outputSet;
    public List<DataOutput> DataOutputs { get; set; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}

public class StartEvent(OutputSet outputSet) : CatchEvent(outputSet);
public class IntermediateCatchEvent(OutputSet outputSet) : CatchEvent(outputSet);

public class BoundaryEvent(OutputSet outputSet, Activity attachedToRef) : CatchEvent(outputSet)
{
    public bool CancelActivity { get; set; }
    public Activity AttachedToRef { get; set; } = attachedToRef;
}
