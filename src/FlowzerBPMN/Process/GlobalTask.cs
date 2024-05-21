namespace BPMN.Process;

public abstract record GlobalTask : ICallableElement
{
    public string? Name { get; init; }
    public InputOutputSpecification? IoSpecification { get; init; }
    public FlowzerList<InputOutputBinding> IoBindings { get; init; } = [];
    public FlowzerList<Interface> SupportedInterfaceRefs { get; init; } = [];
    public FlowzerList<ResourceRole> Resources { get; init; } = [];
}