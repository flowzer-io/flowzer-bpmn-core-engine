using BPMN.Common;
using BPMN.Service;

namespace BPMN.Events;

public class MessageEventDefinition : EventDefinition
{
    public Operation? OperationRef { get; set; }
    public Message? MessageRef { get; set; }
}