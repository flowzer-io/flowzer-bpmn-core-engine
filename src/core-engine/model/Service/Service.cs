using System.ComponentModel.DataAnnotations;
using Common;
using Data;

namespace Service;

public class Operation(Message inMessageRef)
{
    [Required] public string Name { get; set; } = "";
    public string? ImplementationRef { get; set; }
    
    public Message InMessageRef { get; set; } = inMessageRef;
    public Message? OutMessageRef { get; set; }
    public List<InputOutputBinding> IoBindings { get; set; } = [];
}

public class Interface
{
    [Required] public string Name { get; set; } = "";
    public string? ImplementationRef { get; set; }
    
    public List<Operation> Operations { get; set; } = [];
    public List<CallableElement> CallableElements { get; set; } = [];
}