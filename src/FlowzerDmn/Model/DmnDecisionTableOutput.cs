namespace FlowzerDmn.Model;

/// <summary>
/// Eine Ausgabespalte einer Entscheidungstabelle.
/// </summary>
public sealed record DmnDecisionTableOutput
{
    /// <summary>Id der Spalte, sofern vorhanden.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// Name der Spalte. Er wird zum Schluessel im Ergebnisobjekt. Bei mehr als einer
    /// Ausgabespalte verlangt der Parser einen Namen.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>Beschriftung der Spalte fuer die Anzeige.</summary>
    public string? Label { get; init; }

    /// <summary>Typangabe des Ausgabewerts, zum Beispiel <c>string</c> oder <c>number</c>.</summary>
    public string? TypeRef { get; init; }

    /// <summary>
    /// Optionaler Wertebereich. Die Reihenfolge der Eintraege bestimmt die Prioritaet
    /// bei <see cref="DmnHitPolicy.Priority"/> und <see cref="DmnHitPolicy.OutputOrder"/>.
    /// </summary>
    public DmnUnaryTests? OutputValues { get; init; }
}
