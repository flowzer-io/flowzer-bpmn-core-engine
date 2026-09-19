namespace FlowzerDmn.Model;

/// <summary>
/// Verdichtung der gesammelten Treffer einer <see cref="DmnHitPolicy.Collect"/>-Tabelle.
/// </summary>
public enum DmnAggregation
{
    /// <summary>Keine Verdichtung: das Ergebnis bleibt eine Liste.</summary>
    None,

    /// <summary>Summe aller Ausgabewerte.</summary>
    Sum,

    /// <summary>Kleinster Ausgabewert.</summary>
    Min,

    /// <summary>Groesster Ausgabewert.</summary>
    Max,

    /// <summary>Anzahl der passenden Regeln.</summary>
    Count
}
