using BPMN.Activities;
using BPMN.Common;

namespace BPMN.Process;

public abstract class GlobalTask(string name) : CallableElement(name)
{
    public List<ResourceRole> Resources { get; set; } = [];
}