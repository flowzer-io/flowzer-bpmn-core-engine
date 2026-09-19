namespace core_engine.Handler;

/// <summary>
/// Ein Element, das eine Nachricht aussendet: Message-Throw-Event, Message-End-Event und
/// Send-Task. Welche der beiden Ausführungsarten gilt, entscheidet allein das Modell.
///
/// Mit <c>zeebe:taskDefinition/@type</c> verhält sich das Element wie ein Service-Task: Der
/// Token bleibt aktiv, die Geschäftslogik macht daraus einen Auftrag für einen externen
/// Worker. Ohne Auftragstyp korreliert die Engine selbst — sie legt die Nachricht in die
/// Liste der ausgehenden Nachrichten und läuft sofort weiter. Werfen ist nicht blockierend;
/// auf eine Antwort wartet erst ein eigenes Catch-Element.
/// </summary>
internal class MessageThrowHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        if (token.CurrentFlowNode is IFlowzerWorkerTask { Implementation.Length: > 0 })
        {
            return;
        }

        if (MessageOf(token.CurrentFlowNode) is { } messageDefinition)
        {
            processInstance.ThrowMessage(token, messageDefinition);
        }

        token.State = FlowNodeState.Completing;
    }

    /// <summary>
    /// Die ausgesendete Nachricht. <c>null</c> heißt: Das Element trägt weder Nachricht noch
    /// Auftragstyp. Der Fähigkeitsvertrag lehnt das vor der Veröffentlichung ab; zur Laufzeit
    /// bleibt es ein reiner Durchlauf, statt die Instanz an einem Altmodell scheitern zu lassen.
    /// </summary>
    private static MessageDefinition? MessageOf(FlowNode? flowNode) => flowNode switch
    {
        FlowzerIntermediateMessageThrowEvent throwEvent => throwEvent.MessageDefinition,
        FlowzerMessageEndEvent endEvent => endEvent.MessageDefinition,
        SendTask sendTask => sendTask.MessageRef,
        _ => null
    };
}
