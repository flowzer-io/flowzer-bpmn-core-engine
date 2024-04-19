using BPMN.Data;
using BPMN.Foundation;
using BPMN.Service;

namespace BPMN.Common;

public class CallableElement(string name) : RootElement
{
    public string Name { get; set; } = name;
    
    public InputOutputSpecification? IoSpecification { get; set; }
    public List<InputOutputBinding> IoBindings { get; set; } = [];
    public List<Interface> SupportedInterfaceRefs { get; set; } = [];
}