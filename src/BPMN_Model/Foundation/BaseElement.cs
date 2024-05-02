using System.Xml.Serialization;

namespace BPMN_Model.Foundation;

public abstract record BaseElement
{
    public required string Id { get; init; } = "";
    //public Documentation? Documentations { get; init; }
}