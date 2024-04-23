using BPMN.Activities;
using BPMN.Common;

namespace BPMN.Process;

public abstract record GlobalTask : CallableElement
{
    public List<ResourceRole> Resources { get; init; } = [];
}