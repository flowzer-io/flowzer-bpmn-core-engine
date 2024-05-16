using BPMN.Data;
using BPMN.Foundation;
using BPMN.Service;

namespace BPMN.Common;

public interface ICallableElement : IRootElement
{
    public string? Name { get; init; }
    
    public InputOutputSpecification? IoSpecification { get; init; }
    public ImmutableList<InputOutputBinding> IoBindings { get; init; }
    public ImmutableList<Interface> SupportedInterfaceRefs { get; init; }
}