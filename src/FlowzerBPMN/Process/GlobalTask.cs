using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;
using BPMN.Service;

namespace BPMN.Process;

public abstract record GlobalTask : ICallableElement
{
    public string? Name { get; init; }
    public InputOutputSpecification? IoSpecification { get; init; }
    public ImmutableList<InputOutputBinding> IoBindings { get; init; } = [];
    public ImmutableList<Interface> SupportedInterfaceRefs { get; init; } = [];
    public ImmutableList<ResourceRole> Resources { get; init; } = [];
}