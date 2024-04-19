using Activities;

namespace Events;

public class CompensateEventDefinition : EventDefinition
{
    public bool WaitForCompletion { get; set; }
    public Activity? ActivityRef { get; set; }
}