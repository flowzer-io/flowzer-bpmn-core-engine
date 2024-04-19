using System.ComponentModel.DataAnnotations;
using BPMN.Common;
using BPMN.Data;

namespace BPMN.Service;

public class Operation(Message inMessageRef)
{
    [Required] public string Name { get; set; } = "";
    public string? ImplementationRef { get; set; }
    
    public Message InMessageRef { get; set; } = inMessageRef;
    public Message? OutMessageRef { get; set; }
    public List<InputOutputBinding> IoBindings { get; set; } = [];
}