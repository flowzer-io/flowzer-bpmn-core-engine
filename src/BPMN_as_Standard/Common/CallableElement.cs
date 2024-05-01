using BPMN.Data;
using BPMN.Foundation;
using BPMN.Service;

namespace BPMN.Common;

public record CallableElement : RootElement
{
    public required string Name { get; init; }
    
    public InputOutputSpecification? IoSpecification { get; init; }
    public List<InputOutputBinding> IoBindings { get; init; } = [];
    public List<Interface> SupportedInterfaceRefs { get; init; } = [];
}