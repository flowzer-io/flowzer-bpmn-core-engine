using BPMN.Common;

namespace BPMN.Events;

public class ErrorEventDefinition : EventDefinition
{
    public Error? Error { get; set; }
}