using System.ComponentModel.DataAnnotations;
using Common;
using Data;
using Events;
using Foundation;
using Process;
using Service;

namespace Activities;

public class Activity : FlowNode
{
    public bool IsForCompensation { get; set; }
    public int StartQuantity { get; set; }
    public int CompletionQuantity { get; set; }
    
    public InputOutputSpecification? IoSpecification { get; set; }
    public List<DataInputAssociation> DataInputAssociations { get; set; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; set; } = [];
    public List<Property> Properties { get; set; } = [];
    public FlowNode? Default { get; set; }
    public List<ResourceRole> Resources { get; set; } = [];
    public LoopCharacteristics? LoopCharacteristics { get; set; }
    public List<BoundaryEvent> BoundaryEvents { get; set; } = [];
}

public class CallActivity : Activity
{
    public CallableElement? CalledElementRef { get; set; }
}

public class ResourceRole : BaseElement
{
    [Required] public string Name { get; set; } = "";
    
    public Process.Process? Process { get; set; }
    public Resource? ResourceRef { get; set; }
    public List<ResourceParameterBinding> ResourceParameterBindings { get; set; } = [];
    public ResourceAssignmentExpression? ResourceAssignmentExpression { get; set; }
}

public abstract class ResourceParameterBinding(ResourceParameter parameterRef) : BaseElement
{
    public ResourceParameter ParameterRef { get; set; } = parameterRef;
    public Expression? Expression { get; set; }
}

public abstract class ResourceAssignmentExpression
{
    public Expression? Expression { get; set; }
}

public abstract class Task : Activity;

public abstract class LoopCharacteristics;

public class StandardLoopCharacteristics : LoopCharacteristics
{
    public bool TestBefore { get; set; }
}

public class MultiInstanceLoopCharacteristics : LoopCharacteristics
{
    public bool IsSequential { get; set; }
    
    public MultiInstanceBehavior Behavior { get; set; }
}

public enum MultiInstanceBehavior
{
    All, One, Complex, None
}

public class SubProcess : Activity, IFlowElementContainer
{
    public bool TriggeredByEvent { get; set; }
    
    public List<FlowElement> FlowElements { get; set; } = [];
}

public class AdHocSubProcess(Expression completionCondition) : SubProcess
{
    public bool CancelRemainingInstances { get; set; }
    public AdHocOrdering Ordering { get; set; }
    
    public Expression CompletionCondition { get; set; } = completionCondition;
}

public enum AdHocOrdering
{
    Parallel, Sequential
}

public class Transaction(string method, string protocol) : SubProcess
{
    public string Method { get; set; } = method;
    public string Protocol { get; set; } = protocol;
}

public class SendTask : Task
{
    [Required] public string Implementation { get; set; } = "";
    
    public Message? MessageRef { get; set; }
    public Operation? OperationRef { get; set; }
}

public class ReceiveTask : Task
{
    [Required] public string Implementation { get; set; } = "";
    public bool Instantiate { get; set; }
    
    public Message? MessageRef { get; set; }
    public Operation? OperationRef { get; set; }
}

public class ScriptTask : Task
{
    [Required] public string ScriptFormat { get; set; } = "";
    [Required] public string Script { get; set; } = "";
}

public class BusinessRuleTask : Task
{
    [Required] public string Implementation { get; set; } = "";
}

public class ServiceTask : Task
{
    [Required] public string Implementation { get; set; } = "";
}