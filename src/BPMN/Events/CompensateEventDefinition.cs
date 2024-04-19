using BPMN.Activities;

namespace BPMN.Events;

public class CompensateEventDefinition : EventDefinition
{
    public bool WaitForCompletion { get; set; }
    public Activity? ActivityRef { get; set; }
}