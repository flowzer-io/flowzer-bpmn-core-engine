namespace BPMN.Common;

public record ResourceParameter : BaseElement
{
    public required string Name { get; init; }
    public bool IsRequired { get; init; }
    
    public ItemDefinition? Type { get; init; }
}