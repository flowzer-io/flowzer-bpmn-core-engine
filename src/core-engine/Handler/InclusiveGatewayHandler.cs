using core_engine.Exceptions;

namespace core_engine.Handler;

/// <summary>
/// Das inklusive Gateway in seiner ersten Ausbaustufe.
///
/// <b>Split:</b> Jeder Ausgang mit wahrer Bedingung bekommt ein Token; trifft keine zu, greift der
/// Standardfluss. Das ist dieselbe Auswahl, die <see cref="DefaultFlowNodeHandler"/> ohnehin
/// trifft — das exklusive Gateway nimmt davon nur den ersten Treffer, das inklusive alle.
///
/// <b>Join:</b> Die vollstaendige BPMN-Semantik („warten, bis kein weiterer Token diesen Join mehr
/// erreichen kann") braucht eine Vorwaertsanalyse des ganzen Graphen. Diese Stufe merkt sich
/// stattdessen am Split, wie viele Zweige er aktiviert hat
/// (<see cref="Token.InclusiveForkId"/> und <see cref="Token.InclusiveForkSize"/>), und der Join
/// wartet auf genau diese Anzahl. Ein Join ohne zugehoerigen Split verhaelt sich wie ein
/// paralleler Join und wartet auf alle Eingaenge.
/// </summary>
internal class InclusiveGatewayHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        var incomingSequenceFlows = processInstance.GetContainerFlowElements(token)
            .OfType<SequenceFlow>()
            .Where(flow => flow.TargetRef.Id == token.CurrentBaseElement.Id)
            .ToArray();

        // Mit hoechstens einem Eingang gibt es nichts zusammenzufuehren: reiner Split.
        if (incomingSequenceFlows.Length <= 1)
        {
            token.State = FlowNodeState.Completing;
            return;
        }

        var waitingTokens = processInstance.Tokens
            .Where(candidate => candidate.CurrentBaseElement.Id == token.CurrentBaseElement.Id
                                && candidate.ParentTokenId == token.ParentTokenId
                                && candidate.State == FlowNodeState.Active)
            .ToArray();

        var tokensFromSplit = waitingTokens.Where(candidate => candidate.InclusiveForkId is not null).ToArray();
        if (tokensFromSplit.Length > 0)
        {
            var completeFork = tokensFromSplit
                .GroupBy(candidate => candidate.InclusiveForkId!.Value)
                .FirstOrDefault(group => group.Count() >= (group.First().InclusiveForkSize ?? int.MaxValue));

            // Noch fehlt mindestens ein aktivierter Zweig.
            if (completeFork is null) return;

            var forkSize = completeFork.First().InclusiveForkSize!.Value;
            MergeTokens(completeFork.Take(forkSize).ToArray());
            return;
        }

        // Ohne Merkzelle bleibt nur die parallele Auslegung: alle Eingaenge muessen da sein.
        var tokensAtInput = new List<Token>(incomingSequenceFlows.Length);
        foreach (var incomingSequenceFlow in incomingSequenceFlows)
        {
            var tokenAtInput = waitingTokens
                .FirstOrDefault(candidate => candidate.LastSequenceFlow?.Id == incomingSequenceFlow.Id);
            if (tokenAtInput is null) return;
            tokensAtInput.Add(tokenAtInput);
        }

        MergeTokens(tokensAtInput.ToArray());
    }

    public override List<Token>? GenerateOutgoingTokens(FlowzerConfig config, InstanceEngine processInstance,
        Token token)
    {
        var outgoingSequenceFlows = processInstance.GetContainerFlowElements(token)
            .OfType<SequenceFlow>()
            .Where(flow => flow.SourceRef.Id == token.CurrentBaseElement.Id)
            .ToArray();

        var outgoingTokens = base.GenerateOutgoingTokens(config, processInstance, token);

        if (outgoingSequenceFlows.Length <= 1)
        {
            return outgoingTokens;
        }

        if (outgoingTokens is null || outgoingTokens.Count == 0)
        {
            throw new FlowzerRuntimeException(
                $"No condition is true and no default flow exists for inclusive gateway '{token.CurrentBaseElement.Id}'.");
        }

        // Die Merkzelle des Splits: Sie wandert mit den Tokens durch ihre Zweige und sagt dem
        // Join, auf wie viele er zu warten hat.
        var inclusiveForkId = Guid.NewGuid();
        foreach (var outgoingToken in outgoingTokens)
        {
            outgoingToken.InclusiveForkId = inclusiveForkId;
            outgoingToken.InclusiveForkSize = outgoingTokens.Count;
        }

        return outgoingTokens;
    }

    /// <summary>
    /// Fuehrt die eingetroffenen Zweige zu einem Token zusammen. Die Merkzelle des Splits ist
    /// damit erledigt und wird geloescht: Sonst hielte ein spaeterer Join sie faelschlich fuer
    /// seine eigene Verzweigung.
    /// </summary>
    private static void MergeTokens(Token[] tokensToMerge)
    {
        tokensToMerge[0].InclusiveForkId = null;
        tokensToMerge[0].InclusiveForkSize = null;
        tokensToMerge[0].State = FlowNodeState.Completing;

        foreach (var tokenToMerge in tokensToMerge[1..])
        {
            tokenToMerge.State = FlowNodeState.Merged;
        }
    }
}
