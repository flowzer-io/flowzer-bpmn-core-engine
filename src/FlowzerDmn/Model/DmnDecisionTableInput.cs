namespace FlowzerDmn.Model;

/// <summary>
/// Eine Eingabespalte einer Entscheidungstabelle.
/// </summary>
public sealed record DmnDecisionTableInput
{
    /// <summary>Id der Spalte, sofern vorhanden.</summary>
    public string? Id { get; init; }

    /// <summary>Beschriftung der Spalte fuer die Anzeige.</summary>
    public string? Label { get; init; }

    /// <summary>Der FEEL-Ausdruck, der den Eingabewert der Spalte liefert.</summary>
    public required string Expression { get; init; }

    /// <summary>Typangabe des Eingabeausdrucks, zum Beispiel <c>string</c> oder <c>number</c>.</summary>
    public string? TypeRef { get; init; }

    /// <summary>Optionaler Wertebereich. Liegt der Eingabewert ausserhalb, bricht die Auswertung ab.</summary>
    public DmnUnaryTests? InputValues { get; init; }
}
