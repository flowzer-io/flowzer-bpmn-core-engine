namespace FlowzerDmn.Model;

/// <summary>
/// Eine Entscheidungstabelle: Spalten, Zeilen und die Trefferregel, nach der aus den
/// passenden Zeilen ein Ergebnis wird.
/// </summary>
public sealed record DmnDecisionTable : DmnDecisionLogic
{
    /// <summary>Trefferregel der Tabelle. Fehlt sie in der Datei, gilt <see cref="DmnHitPolicy.Unique"/>.</summary>
    public required DmnHitPolicy HitPolicy { get; init; }

    /// <summary>Verdichtung bei <see cref="DmnHitPolicy.Collect"/>; sonst <see cref="DmnAggregation.None"/>.</summary>
    public DmnAggregation Aggregation { get; init; } = DmnAggregation.None;

    /// <summary>Die Eingabespalten in Dokumentreihenfolge.</summary>
    public required IReadOnlyList<DmnDecisionTableInput> Inputs { get; init; }

    /// <summary>Die Ausgabespalten in Dokumentreihenfolge.</summary>
    public required IReadOnlyList<DmnDecisionTableOutput> Outputs { get; init; }

    /// <summary>Die Regeln in Dokumentreihenfolge.</summary>
    public required IReadOnlyList<DmnDecisionRule> Rules { get; init; }
}
