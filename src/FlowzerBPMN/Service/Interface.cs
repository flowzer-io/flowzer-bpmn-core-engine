using BPMN.Common;

namespace BPMN.Service;

public record Interface
{
    public required string Name { get; init; }
    public string? ImplementationRef { get; init; }
    
    public FlowzerList<Operation> Operations { get; init; } = [];
    public FlowzerList<ICallableElement> CallableElements { get; init; } = [];
}