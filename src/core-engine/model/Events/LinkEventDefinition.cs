namespace Events;

public class LinkEventDefinition : EventDefinition
{
    public string Name { get; set; } = "";
    public List<LinkEventDefinition> Sources { get; set; } = [];
    public LinkEventDefinition? Target { get; set; }
}