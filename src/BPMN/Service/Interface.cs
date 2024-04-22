using BPMN.Common;

namespace BPMN.Service;

public record Interface
{
    public required string Name { get; init; }
    public string? ImplementationRef { get; init; }
    
    public List<Operation> Operations { get; init; } = [];
    public List<CallableElement> CallableElements { get; init; } = [];
}