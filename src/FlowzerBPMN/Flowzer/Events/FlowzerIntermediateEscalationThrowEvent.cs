namespace BPMN.Flowzer.Events;

/// <summary>
/// Ein Zwischenereignis mit <c>escalationEventDefinition</c>. Es meldet eine Eskalation nach
/// aussen und laeuft sofort weiter: Anders als ein Fehler unterbricht eine Eskalation den
/// werfenden Pfad nicht.
/// </summary>
public record FlowzerIntermediateEscalationThrowEvent : IntermediateThrowEvent
{
    public Escalation? Escalation { get; init; }
}
