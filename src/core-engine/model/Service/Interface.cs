using System.ComponentModel.DataAnnotations;
using Common;

namespace Service;

public class Interface
{
    [Required] public string Name { get; set; } = "";
    public string? ImplementationRef { get; set; }
    
    public List<Operation> Operations { get; set; } = [];
    public List<CallableElement> CallableElements { get; set; } = [];
}