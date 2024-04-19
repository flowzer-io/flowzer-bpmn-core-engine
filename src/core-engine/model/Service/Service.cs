using System.ComponentModel.DataAnnotations;
using Common;

namespace Service;

public class Operation
{
    [Required] public string Name { get; set; } = "";
    public string? ImplementationRef { get; set; }
    
    public Message InMessageRef { get; set; }
    public Message? OutMessageRef { get; set; }
}

public class Interface
{
    [Required] public string Name { get; set; } = "";
    public string? ImplementationRef { get; set; }
    
    public List<Operation> Operations { get; set; } = [];
}