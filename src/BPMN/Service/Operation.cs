using BPMN.Common;
using BPMN.Data;

namespace BPMN.Service;

public class Operation
{
    public required string Name { get; set; }
    public string? ImplementationRef { get; set; }
    
    public required Message InMessageRef { get; set; }
    public Message? OutMessageRef { get; set; }
    public List<InputOutputBinding> IoBindings { get; set; } = [];
}