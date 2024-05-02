using System.Xml.Serialization;

namespace BPMN_Model.Foundation;

public abstract record BaseElement
{
    [XmlAttribute("id")]
    public required string Id { get; init; } = "";
    //public Documentation? Documentations { get; init; }
}