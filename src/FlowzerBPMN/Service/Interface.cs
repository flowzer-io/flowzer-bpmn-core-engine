using BPMN.Common;

namespace BPMN.Service;

public record Interface
{
    public required string Name { get; init; }
    public string? ImplementationRef { get; init; }
    
    public ImmutableList<Operation> Operations { get; init; } = [];
    public ImmutableList<ICallableElement> CallableElements { get; init; } = [];
}