using BPMN_Model.Common;
using BPMN_Model.Foundation;

namespace BPMN_Model.Process;

public record Process : BaseElement
{
    public required Model Model { get; init; }
}