using core_engine.Exceptions;

namespace core_engine.Handler;

/// <summary>
/// Ein ereignisbasiertes Gateway stellt alle seine Folgeereignisse gleichzeitig scharf. Umgesetzt
/// wird das mit gewoehnlichen Tokens: je Ausgang eines, das wie jedes andere Catch-Element
/// wartet — und damit auch dieselbe Message-, Signal- oder Timer-Subscription erzeugt.
///
/// Zusammengehalten werden sie von <see cref="Token.EventGroupId"/>. Trifft eines der Ereignisse
/// ein, zieht die Lauf-Schleife die uebrigen Mitglieder derselben Gruppe zurueck; deren
/// Subscriptions verschwinden damit beim naechsten Speichern von selbst.
/// </summary>
internal class EventBasedGatewayHandler : DefaultFlowNodeHandler
{
    public override List<Token>? GenerateOutgoingTokens(FlowzerConfig config, InstanceEngine processInstance,
        Token token)
    {
        var outgoingTokens = base.GenerateOutgoingTokens(config, processInstance, token);

        if (outgoingTokens is null || outgoingTokens.Count == 0)
        {
            throw new FlowzerRuntimeException(
                $"The event based gateway '{token.CurrentBaseElement.Id}' has no outgoing sequence flow.");
        }

        var eventGroupId = Guid.NewGuid();
        foreach (var outgoingToken in outgoingTokens)
        {
            outgoingToken.EventGroupId = eventGroupId;
        }

        return outgoingTokens;
    }
}
