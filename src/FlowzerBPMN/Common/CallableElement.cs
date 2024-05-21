namespace BPMN.Common;

public interface ICallableElement : IRootElement
{
    public string? Name { get; init; }
    
    public InputOutputSpecification? IoSpecification { get; init; }
    public FlowzerList<InputOutputBinding>? IoBindings { get; init; }
    public FlowzerList<Interface>? SupportedInterfaceRefs { get; init; }
}