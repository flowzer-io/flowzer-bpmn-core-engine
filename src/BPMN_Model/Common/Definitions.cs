using System.Xml.Serialization;
using BPMN_Model.Foundation;

namespace BPMN_Model.Common;

public record Definitions : BaseElement
{
    public int? Version { get; set; }
    public List<Process.Process> Processes { get; set; } = [];
}