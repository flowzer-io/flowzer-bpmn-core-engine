namespace Foundation;

public class Relationship : BaseElement
{
    public string Type { get; set; } = "";
    public RelationshipDirection RelationshipDirection { get; set; }
}