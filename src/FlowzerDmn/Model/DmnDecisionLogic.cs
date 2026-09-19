namespace FlowzerDmn.Model;

/// <summary>
/// Gemeinsame Basis der Entscheidungslogiken. In dieser Ausbaustufe gibt es genau zwei:
/// <see cref="DmnDecisionTable"/> und <see cref="DmnLiteralExpression"/>. Jede andere
/// boxed expression lehnt der Parser mit einer
/// <see cref="Exceptions.DmnUnsupportedException"/> ab.
/// </summary>
public abstract record DmnDecisionLogic
{
    /// <summary>Id des Logik-Elements, sofern die Datei eine mitbringt.</summary>
    public string? Id { get; init; }
}
