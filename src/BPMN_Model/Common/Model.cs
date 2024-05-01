using BPMN_Model.Foundation;

namespace BPMN_Model.Common;

public record Model : BaseElement
{
    public int? Version { get; set; }
    public List<Process.Process> Processes { get; init; } = [];
}