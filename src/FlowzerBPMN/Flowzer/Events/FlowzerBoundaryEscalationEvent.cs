namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Boundary-Event mit <c>escalationEventDefinition</c>. Es faengt eine Eskalation aus der
/// Aktivitaet, an der es haengt; ohne <c>escalationRef</c> faengt es jede. Anders als beim Fehler
/// sind beide Arten zulaessig: unterbrechend zieht die Aktivitaet zurueck, nicht unterbrechend
/// laesst sie weiterlaufen und oeffnet zusaetzlich den Eskalationspfad.
/// </summary>
public record FlowzerBoundaryEscalationEvent : BoundaryEvent
{
    public Escalation? Escalation { get; init; }
}
