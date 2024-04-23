namespace BPMN.Data;

public record Property : ItemAwareElement
{
    public required string Name { get; init; }
}