using core_engine.Exceptions;

namespace core_engine;

/// <summary>
/// Eine Entscheidung, die die Engine bereitgestellt hat und die noch gerechnet werden muss.
/// </summary>
/// <param name="TokenId">Das wartende Token der Instanz.</param>
/// <param name="DecisionId">Die <c>zeebe:calledDecision/@decisionId</c> der Entscheidung.</param>
/// <param name="Variables">Die Variablen, die der Entscheidung zur Verfuegung stehen.</param>
public sealed record PendingDecision(Guid TokenId, string DecisionId, Variables Variables);

public partial class InstanceEngine
{
    private readonly List<PendingDecision> _pendingDecisions = [];

    /// <summary>
    /// Die Tokens, fuer die dieser Engine-Lauf bereits eine Entscheidung bereitgestellt hat —
    /// auch dann, wenn sie inzwischen abgeholt wurde. Anders als bei der Call Activity gibt es
    /// hier keine dauerhafte Merkfaehigkeit am Token: Eine Entscheidung wird lokal und in
    /// derselben Transaktion gerechnet, ein zweites Anfordern nach einem Neuladen kostet also
    /// nichts ausser Rechenzeit und hat keine Nebenwirkung nach aussen.
    /// </summary>
    private readonly HashSet<Guid> _requestedDecisionTokenIds = [];

    /// <summary>
    /// Die Entscheidungen, die diese Instanz seit dem letzten Abholen bereitgestellt hat.
    ///
    /// Wie bei den Call Activities wertet die Engine selbst nichts aus: Sie kennt weder das
    /// DMN-Modell noch den Auswerter. Sie legt nur bereit; rechnen muss die Geschaeftslogik
    /// (in der Web-API in derselben Transaktion wie das Speichern der Instanz). Die Liste
    /// gehoert zu genau einem Engine-Lauf und wird nicht mit dem Tokenstand persistiert.
    /// </summary>
    public IReadOnlyList<PendingDecision> PendingDecisions => _pendingDecisions;

    /// <summary>
    /// Gibt die ausstehenden Entscheidungen heraus und leert die Liste, damit dieselbe
    /// Entscheidung nicht zweimal gerechnet wird.
    /// </summary>
    public IReadOnlyList<PendingDecision> TakePendingDecisions()
    {
        var decisions = _pendingDecisions.ToArray();
        _pendingDecisions.Clear();

        return decisions;
    }

    /// <summary>
    /// Die Tokens, die auf eine Entscheidung warten. Ein Business-Rule-Task mit Auftragstyp
    /// steht hier bewusst nicht: Er ist ein Auftrag an einen Worker und erscheint deshalb in
    /// <see cref="GetActiveServiceTasks"/>.
    /// </summary>
    public IEnumerable<Token> GetWaitingBusinessRuleTasks() => Tokens
        .Where(token => token.State == FlowNodeState.Active
            && token.CurrentFlowNode is BusinessRuleTask { Implementation.Length: 0 });

    /// <summary>
    /// Schliesst ein wartendes Business-Rule-Task-Token mit dem Ergebnis der Entscheidung ab.
    ///
    /// Das Ergebnis landet unter <paramref name="resultVariable"/> im Prozesskontext. Ein
    /// <c>zeebe:ioMapping</c>-Ausgang greift zusaetzlich (siehe <c>PrepareOutputData</c>) und
    /// kann daraus einzelne Werte an anderer Stelle ablegen — genau wie bei der Call Activity.
    /// </summary>
    /// <param name="tokenId">Das wartende Token.</param>
    /// <param name="resultVariable">Der Name, unter dem das Ergebnis im Prozess steht.</param>
    /// <param name="value">Der Wert aus <c>DmnDecisionResult.Value</c>.</param>
    public void CompleteDecision(Guid tokenId, string resultVariable, object? value)
    {
        var token = GetToken(tokenId);
        RequireWaitingBusinessRuleTask(token);

        var outputData = new Variables();
        ((IDictionary<string, object?>)outputData)[resultVariable] = ToProcessValue(value);
        token.OutputData = outputData;
        token.State = FlowNodeState.Completing;

        Run();
    }

    /// <summary>
    /// Stellt die Entscheidung eines erreichten Business-Rule-Task-Tokens bereit.
    ///
    /// Die Variablen folgen dem Modell: Liegt ein <c>zeebe:ioMapping</c>-Eingang vor, hat
    /// <c>PrepareInputData</c> ihn beim Erreichen des Knotens bereits ausgewertet und in
    /// <see cref="Token.Variables"/> gelegt — dann gilt genau diese Auswahl. Ohne Zuordnung
    /// bekommt die Entscheidung bewusst den ganzen Prozesskontext.
    ///
    /// Das ist der Unterschied zur ausgehenden Nachricht und zur Call Activity, die ohne
    /// Zuordnung nichts mitnehmen: Die Entscheidungstabelle laeuft lokal in derselben
    /// Transaktion, es verlaesst nichts den Server. Eine Tabelle, die nach einer Variablen
    /// fragt, soll sie finden, statt an einer vergessenen Zuordnung leer auszugehen.
    /// </summary>
    internal void RequestDecision(Token token, BusinessRuleTask task)
    {
        if (!_requestedDecisionTokenIds.Add(token.Id))
        {
            return;
        }

        var decisionId = task.FlowzerCalledDecisionId;
        if (string.IsNullOrWhiteSpace(decisionId))
        {
            // Die Veroeffentlichungspruefung faengt das bereits ab; ein aelteres Modell aus der
            // Ablage koennte trotzdem hier ankommen. Dann ist klar zu sagen, was fehlt.
            throw new FlowzerRuntimeException(
                $"Der Business-Rule-Task {task.Id} nennt weder eine Decision noch einen Auftragstyp.");
        }

        var variables = new Variables();
        var target = (IDictionary<string, object?>)variables;

        foreach (var (key, value) in token.Variables ?? GetProcessToken(token).Variables ?? new Variables())
        {
            target[key] = value;
        }

        _pendingDecisions.Add(new PendingDecision(token.Id, decisionId, variables));
    }

    private static BusinessRuleTask RequireWaitingBusinessRuleTask(Token token)
    {
        if (token.State != FlowNodeState.Active
            || token.CurrentFlowNode is not BusinessRuleTask { Implementation.Length: 0 } businessRuleTask)
        {
            throw new FlowzerRuntimeException(
                $"Token {token.Id} wartet nicht an einer Entscheidung.");
        }

        return businessRuleTask;
    }

    /// <summary>
    /// Setzt das Ergebnis einer Entscheidung in eine Prozessvariable um.
    ///
    /// Der DMN-Kern liefert seine Ausgabespalten als <see cref="IReadOnlyDictionary{TKey,TValue}"/>
    /// und die Listen-Trefferregeln als Liste davon. Die Engine fuehrt ihre Variablen dagegen
    /// als <c>Variables</c> (ein <see cref="System.Dynamic.ExpandoObject"/>). Ohne diese
    /// Umsetzung stuende das Ergebnis zwar im Prozess, waere aber in keinem FEEL-Ausdruck
    /// erreichbar: <c>ergebnis.rabatt</c> an einer Gateway-Bedingung liefe ins Leere.
    ///
    /// Skalare bleiben, was sie sind; <c>null</c> bleibt <c>null</c>.
    /// </summary>
    private static object? ToProcessValue(object? value)
    {
        switch (value)
        {
            case null:
            // Eine Zeichenkette ist zwar aufzaehlbar, aber kein Wertebehaelter.
            case string:
                return value;

            case IReadOnlyDictionary<string, object?> readOnlyEntries:
                return ToVariables(readOnlyEntries);

            // Deckt auch ein ExpandoObject ab, das bereits die richtige Gestalt hat, dessen
            // Inhalt aber noch umzusetzen sein kann.
            case IDictionary<string, object?> entries:
                return ToVariables(entries);

            case System.Collections.IEnumerable items:
                return items.Cast<object?>().Select(ToProcessValue).ToList();

            default:
                return value;
        }
    }

    private static Variables ToVariables(IEnumerable<KeyValuePair<string, object?>> entries)
    {
        var variables = new Variables();
        var target = (IDictionary<string, object?>)variables;

        foreach (var (key, value) in entries)
        {
            target[key] = ToProcessValue(value);
        }

        return variables;
    }
}
