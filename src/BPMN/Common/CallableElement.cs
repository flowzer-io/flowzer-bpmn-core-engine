using BPMN.Data;
using BPMN.Foundation;
using BPMN.Service;

namespace BPMN.Common;

public class CallableElement : RootElement
{
    public required string Name { get; set; }
    
    public InputOutputSpecification? IoSpecification { get; set; }
    public List<InputOutputBinding> IoBindings { get; set; } = [];
    public List<Interface> SupportedInterfaceRefs { get; set; } = [];
}