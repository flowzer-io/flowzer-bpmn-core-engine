namespace core_engine.Handler;

/// <summary>
/// Ein werfendes Eskalationsereignis — als Zwischenereignis oder als Ende.
///
/// Eskalation ist nicht blockierend: Die Meldung geht nach aussen, und der Pfad laeuft weiter.
/// Nur wenn ein unterbrechender Faenger den umschliessenden Scope zurueckzieht, endet er hier —
/// dann ist dieses Token bereits zurueckgezogen und wird nicht wiederbelebt.
/// </summary>
public class EscalationThrowHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        var escalation = token.CurrentBaseElement switch
        {
            FlowzerIntermediateEscalationThrowEvent throwEvent => throwEvent.Escalation,
            FlowzerEscalationEndEvent endEvent => endEvent.Escalation,
            _ => null
        };

        processInstance.RaiseEscalation(token, escalation?.EscalationCode);

        // Ein unterbrechender Faenger hat dieses Token soeben zurueckgezogen; dann bleibt es dabei.
        if (token.State == FlowNodeState.Active)
        {
            token.State = FlowNodeState.Completing;
        }
    }
}
