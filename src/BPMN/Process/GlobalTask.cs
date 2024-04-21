using BPMN.Activities;
using BPMN.Common;

namespace BPMN.Process;

public abstract class GlobalTask : CallableElement
{
    public List<ResourceRole> Resources { get; set; } = [];
}