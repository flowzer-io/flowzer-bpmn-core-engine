namespace BPMN.Service;

public record Operation
{
    public required string Name { get; init; }
    public string? ImplementationRef { get; init; }
    
    public required MessageDefinition InMessageDefinitionRef { get; init; }
    public MessageDefinition? OutMessageRef { get; init; }
    public FlowzerList<InputOutputBinding> IoBindings { get; init; } = [];
}