using Common;
using Service;

namespace Events;

public class MessageEventDefinition : EventDefinition
{
    public Operation? OperationRef { get; set; }
    public Message? MessageRef { get; set; }
}