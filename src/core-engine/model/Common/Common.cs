using System.ComponentModel.DataAnnotations;
using Activities;
using Artifacts;
using Data;
using Foundation;
using Process;
using Service;

namespace Common;

public abstract class CorrelationSubscription;

public class Error
{
    [Required] public string Name { get; set; } = "";
    public string? ErrorCode { get; set; }
    
    public ItemDefinition? StructureRef { get; set; }
}

public class ItemDefinition : RootElement
{
    public ItemKind ItemKind { get; set; }
    public object? StructureRef { get; set; }
    public bool IsCollection { get; set; }
}

public enum ItemKind
{
    Information, Physical
}

public interface IFlowElementContainer : IBaseElement
{
    public List<FlowElement> FlowElements { get; set; }
}

public class CallableElement(string name) : RootElement
{
    public string Name { get; set; } = name;
    
    public InputOutputSpecification? IoSpecification { get; set; }
    public List<InputOutputBinding> IoBindings { get; set; } = [];
    public List<Interface> SupportedInterfaceRefs { get; set; } = [];
}

public abstract class FlowElement : BaseElement
{
    [Required] public string Name { get; set; } = "";
    [Required] public IFlowElementContainer Container { get; set; }
    public Auditing? Auditing { get; set; }
    public Monitoring? Monitoring { get; set; }
    public List<CategoryValue> CategoryValueRefs { get; set; } = [];
}

public abstract class FlowNode : FlowElement;

public class SequenceFlow(FlowNode sourceRef, FlowNode targetRef) : FlowElement
{
    public bool IsImmediate { get; set; }
    public FlowNode SourceRef { get; set; } = sourceRef;
    public FlowNode TargetRef { get; set; } = targetRef;
    public Expression? ConditionExpression { get; set; }
}

public abstract class Expression : BaseElement;

public class FormalExpression : Expression
{
    [Required] public string Body { get; set; } = "";
    [Required] public string Language { get; set; } = "";
    
    public ItemDefinition? EvaluatesToTypeRef { get; set; }
}

public class Message : RootElement
{
    [Required] public string Name { get; set; } = "";
    public ItemDefinition? ItemRef { get; set; }
}

public class Resource : RootElement
{
    [Required] public string Name { get; set; } = "";
    
    public List<ResourceParameter> ResourceParameters { get; set; } = [];
}

public class ResourceParameter : BaseElement
{
    [Required] public string Name { get; set; } = "";
    public bool IsRequired { get; set; }
    
    public ItemDefinition? Type { get; set; }
}