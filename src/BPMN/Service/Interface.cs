using BPMN.Common;

namespace BPMN.Service;

public class Interface
{
    public required string Name { get; set; }
    public string? ImplementationRef { get; set; }
    
    public List<Operation> Operations { get; set; } = [];
    public List<CallableElement> CallableElements { get; set; } = [];
}