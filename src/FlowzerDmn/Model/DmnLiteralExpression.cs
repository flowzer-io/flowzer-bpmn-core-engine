namespace FlowzerDmn.Model;

/// <summary>
/// Ein einzelner FEEL-Ausdruck als Entscheidungslogik. Das Ergebnis der Decision ist
/// der Wert des Ausdrucks, typisiert ueber das <c>typeRef</c> der Decision-Variable.
/// </summary>
public sealed record DmnLiteralExpression : DmnDecisionLogic
{
    /// <summary>Der Ausdruckstext ohne fuehrendes Gleichheitszeichen.</summary>
    public required string Text { get; init; }

    /// <summary>Angegebene Ausdruckssprache, falls die Datei eine nennt (meist FEEL).</summary>
    public string? ExpressionLanguage { get; init; }
}
