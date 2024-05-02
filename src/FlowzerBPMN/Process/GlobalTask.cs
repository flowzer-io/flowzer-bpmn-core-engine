using BPMN.Activities;
using BPMN.Common;
using BPMN.Data;
using BPMN.Service;

namespace BPMN.Process;

public abstract record GlobalTask : ICallableElement
{
    public string? Name { get; init; }
    public InputOutputSpecification? IoSpecification { get; init; }
    public List<InputOutputBinding> IoBindings { get; init; } = [];
    public List<Interface> SupportedInterfaceRefs { get; init; } = [];
    public List<ResourceRole> Resources { get; init; } = [];
}