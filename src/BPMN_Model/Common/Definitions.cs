using BPMN_Model.Foundation;

namespace BPMN_Model.Common;

public record Definitions : BaseElement
{
    public List<Process.Process> Processes { get; init; } = [];
}