using BPMN.Foundation;
using core_engine.Extensions;

namespace core_engine;

public partial class InstanceEngine
{
    /// <summary>
    /// Fuehrt einen geprueften Betriebseingriff am Tokenstand aus. Bewusst hier und nicht in
    /// <see cref="InstanceModification"/>: Zurueckziehen, Neuanlegen und Weiterlaufen sind
    /// Engine-Inneres, und der Eingriff soll keine zweite Fassung davon mitbringen.
    ///
    /// Die Reihenfolge ist Absicht. Zuerst die Variablen: Ein Gateway, auf das gleich ein Token
    /// gesetzt wird, muss den korrigierten Wert sehen und nicht den alten. Danach jede
    /// Verschiebung, erst dann ein einziger Lauf — sonst liefe die Instanz zwischen zwei
    /// Verschiebungen derselben Anfrage kurz mit einem halben Stand.
    /// </summary>
    internal void ApplyModification(InstanceModificationPlan plan)
    {
        var masterToken = MasterToken;

        foreach (var (name, value) in plan.Request.VariablesToSet ?? new Dictionary<string, object?>())
        {
            SetModifiedVariable(masterToken, name, value);
        }

        // Nach dem Schreiben: Nennt eine Anfrage denselben Namen in beiden Abschnitten, ist das
        // Entfernen die deutlichere Ansage und gewinnt.
        foreach (var name in plan.Request.VariablesToRemove ?? [])
        {
            RemoveModifiedVariable(masterToken, name);
        }

        foreach (var move in plan.Moves)
        {
            // Derselbe Weg wie beim Fangen eines BPMN-Fehlers: Mit dem Scope verschwinden auch
            // Nachrichten-, Signal- und Timer-Anmeldungen, weil die nur an lebenden Tokens haengen.
            WithdrawScope(move.SourceToken);
            Tokens.Add(CreateModifiedToken(masterToken, move));
        }

        Run();
    }

    /// <summary>
    /// Baut den Token am Zielknoten so, wie ihn ein Sequenzfluss hinterlassen haette: im Zustand
    /// <see cref="FlowNodeState.Ready"/>, mit aufgeloesten Ausdruecken und scharfen
    /// Boundary-Events des Ziels. Eine neue Kennung ist der Punkt — die verlassene Aufgabe ist
    /// samt ihrer Kennung weg, am Ziel beginnt Arbeit von vorn.
    /// </summary>
    private Token CreateModifiedToken(Token masterToken, InstanceModificationPlan.PlannedMove move)
    {
        var processVariables = masterToken.Variables ?? new Variables();

        return new Token
        {
            ProcessInstanceId = masterToken.ProcessInstanceId,
            ParentTokenId = masterToken.Id,
            CurrentBaseElement = move.TargetFlowNode.ApplyResolveExpression<FlowNode>(
                FlowzerConfig.ExpressionHandler.ResolveString, processVariables),
            ActiveBoundaryEvents = [.. Process.FlowElements
                .OfType<BoundaryEvent>()
                .Where(boundaryEvent => string.Equals(
                    boundaryEvent.AttachedToRef.Id, move.TargetFlowNode.Id, StringComparison.Ordinal))
                .Select(boundaryEvent => boundaryEvent.ApplyResolveExpression<BoundaryEvent>(
                    FlowzerConfig.ExpressionHandler.ResolveString, processVariables))],
            State = FlowNodeState.Ready,
            // Woher der Schritt kommt, gehoert zum Vorgang: Der Verlauf soll den verlassenen
            // Knoten nennen koennen, auch wenn der Sequenzfluss ihn nicht hergibt.
            PreviousToken = move.SourceToken
        };
    }

    /// <summary>
    /// Schreibt eine Variable auf die Ebene, auf der sie heute liegt. Die Prozessebene hat
    /// Vorrang: Was dort steht, lesen alle Schritte. Nur wenn die Variable dort unbekannt ist
    /// und genau an einem wartenden Schritt liegt — etwa als abgebildete Eingabe eines
    /// Service-Tasks —, wird sie dort korrigiert. Sonst entsteht sie auf der Prozessebene.
    /// </summary>
    private void SetModifiedVariable(Token masterToken, string name, object? value)
    {
        var scopeToken = VariableScopeToken(masterToken, name) ?? masterToken;
        scopeToken.Variables ??= new Variables();
        ((IDictionary<string, object?>)scopeToken.Variables)[name] = value;
    }

    /// <summary>
    /// Entfernt eine Variable ueberall auf der Prozessebene. Bliebe eine Kopie an einem
    /// wartenden Schritt stehen, waere sie nach dem Eingriff immer noch lesbar — und niemand
    /// haette das so verstanden.
    /// </summary>
    private void RemoveModifiedVariable(Token masterToken, string name)
    {
        foreach (var token in InstanceModification.ProcessScopeTokens(masterToken, this))
        {
            if (token.Variables is null) continue;
            ((IDictionary<string, object?>)token.Variables).Remove(name);
        }
    }

    private Token? VariableScopeToken(Token masterToken, string name) => InstanceModification
        .ProcessScopeTokens(masterToken, this)
        .FirstOrDefault(token => token.Variables.HasProperty(name));
}
