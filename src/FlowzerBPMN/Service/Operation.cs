namespace BPMN.Service;

public record Operation
{
    public required string Name { get; init; }
    public string? ImplementationRef { get; init; }
    
    public required Message InMessageRef { get; init; }
    public Message? OutMessageRef { get; init; }
    public FlowzerList<InputOutputBinding> IoBindings { get; init; } = [];
}