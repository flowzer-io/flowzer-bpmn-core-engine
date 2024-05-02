using BPMN.Data;
using BPMN.Foundation;
using BPMN.Service;

namespace BPMN.Common;

public interface ICallableElement : IRootElement
{
    public string? Name { get; init; }
    
    public InputOutputSpecification? IoSpecification { get; init; }
    public List<InputOutputBinding> IoBindings { get; init; }
    public List<Interface> SupportedInterfaceRefs { get; init; }
}