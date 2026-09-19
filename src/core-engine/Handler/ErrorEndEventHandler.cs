namespace core_engine.Handler;

/// <summary>
/// Ein Error-End-Event beendet seinen eigenen Pfad und loest gleichzeitig einen BPMN-Fehler mit
/// dem Code des referenzierten <c>bpmn:error</c> aus. Ein ausgehender Sequenzfluss existiert
/// nicht; der Fehler entscheidet, wo es weitergeht.
/// </summary>
public class ErrorEndEventHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        var errorEndEvent = (FlowzerErrorEndEvent)token.CurrentBaseElement;

        // Erst abschliessen, dann werfen: So zieht das Unterbrechen des Scopes dieses Token
        // nicht mit zurueck, und der Knoten bleibt im Laufzeitverlauf als erreicht sichtbar.
        token.State = FlowNodeState.Completed;
        processInstance.RaiseBpmnError(token, errorEndEvent.Error?.ErrorCode, null, catchAtOriginActivity: false);
    }
}
