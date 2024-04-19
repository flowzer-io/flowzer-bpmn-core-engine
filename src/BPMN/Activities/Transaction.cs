using BPMN.Common;

namespace BPMN.Activities;

public class Transaction(string name, IFlowElementContainer container, string method, string protocol) : SubProcess(name, container)
{
    public string Method { get; set; } = method;
    public string Protocol { get; set; } = protocol;
}