using BPMN.Events;

namespace BPMN.Flowzer;

public record FlowzerTimerStartEvent : StartEvent
{  
    public required string TimerDefinition { get; set; }
};