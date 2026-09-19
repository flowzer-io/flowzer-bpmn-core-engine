namespace FlowzerDmn.Model;

/// <summary>
/// Eine Zeile der Entscheidungstabelle.
/// </summary>
/// <remarks>
/// <see cref="InputEntries"/> und <see cref="OutputEntries"/> stehen Spalte fuer Spalte in
/// derselben Reihenfolge wie <see cref="DmnDecisionTable.Inputs"/> und
/// <see cref="DmnDecisionTable.Outputs"/>; der Parser stellt das sicher.
/// </remarks>
public sealed record DmnDecisionRule
{
    /// <summary>Id der Regel. Sie taucht in <see cref="Evaluation.DmnDecisionResult.MatchedRules"/> wieder auf.</summary>
    public required string Id { get; init; }

    /// <summary>Freitext aus dem <c>description</c>-Element der Regel.</summary>
    public string? Description { get; init; }

    /// <summary>Die Bedingungen der Zeile als FEEL-Unary-Tests. Leer oder <c>-</c> passt immer.</summary>
    public required IReadOnlyList<string> InputEntries { get; init; }

    /// <summary>Die Ausgaben der Zeile als FEEL-Ausdruecke. Ein leerer Eintrag liefert <c>null</c>.</summary>
    public required IReadOnlyList<string> OutputEntries { get; init; }
}
