using BPMN.Common;
using BPMN.Service;

namespace BPMN.Events;

public record MessageEventDefinition : EventDefinition
{
    public Operation? OperationRef { get; init; }
    public Message? MessageRef { get; init; }
}