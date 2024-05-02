namespace BPMN.Foundation;

public record Relationship : BaseElement
{
    public string Type { get; init; } = "";
    public RelationshipDirection RelationshipDirection { get; init; }
}