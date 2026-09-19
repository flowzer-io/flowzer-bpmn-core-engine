namespace FlowzerDmn.Evaluation;

/// <summary>
/// Das Ergebnis einer ausgewerteten Decision.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> haengt an der Trefferregel:
/// </para>
/// <list type="bullet">
/// <item>
/// UNIQUE, FIRST, PRIORITY, ANY: ein <see cref="IReadOnlyDictionary{TKey,TValue}"/> mit
/// den Ausgabespalten, auch wenn es nur eine gibt — das ist die Konvention von Camunda 8
/// und macht den Business-Rule-Task spaeter unabhaengig davon, ob jemand eine zweite
/// Spalte ergaenzt. Ohne Treffer: <c>null</c>.
/// </item>
/// <item>COLLECT ohne Verdichtung, RULE ORDER, OUTPUT ORDER: eine Liste solcher Objekte, ohne Treffer leer.</item>
/// <item>COLLECT mit SUM/MIN/MAX/COUNT: ein einzelner Zahlenwert.</item>
/// <item>literalExpression: der Wert des Ausdrucks, ohne Umhuellung.</item>
/// </list>
/// </remarks>
public sealed record DmnDecisionResult
{
    /// <summary>Die Id der ausgewerteten Decision.</summary>
    public required string DecisionId { get; init; }

    /// <summary>
    /// Die Ids der Regeln, die getroffen haben, in der Reihenfolge, in der sie im Ergebnis
    /// stehen. Bei PRIORITY und OUTPUT ORDER ist das die Rangfolge, sonst die Regelreihenfolge.
    /// Bei einem Literal-Ausdruck bleibt die Liste leer.
    /// </summary>
    public required IReadOnlyList<string> MatchedRules { get; init; }

    /// <summary>Das Ergebnis gemaess Trefferregel.</summary>
    public required object? Value { get; init; }

    /// <summary>
    /// Die Ergebnisse der benoetigten Decisions, auch die der weiter unten liegenden,
    /// nach Decision-Id. So laesst sich eine Kette im Nachhinein nachvollziehen.
    /// </summary>
    public required IReadOnlyDictionary<string, DmnDecisionResult> RequiredResults { get; init; }
}
