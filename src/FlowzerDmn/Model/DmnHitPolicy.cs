namespace FlowzerDmn.Model;

/// <summary>
/// Trefferregel einer Entscheidungstabelle. Sie entscheidet, was aus den passenden
/// Regeln als Ergebnis wird.
/// </summary>
public enum DmnHitPolicy
{
    /// <summary>Hoechstens eine Regel darf passen; mehrere Treffer sind ein Modellfehler.</summary>
    Unique,

    /// <summary>Die erste passende Regel in Dokumentreihenfolge gewinnt.</summary>
    First,

    /// <summary>Die passende Regel mit der hoechsten Ausgabepriorität gewinnt (<c>outputValues</c>).</summary>
    Priority,

    /// <summary>Mehrere Treffer sind erlaubt, muessen aber dieselbe Ausgabe liefern.</summary>
    Any,

    /// <summary>Alle Treffer werden gesammelt, optional zu einer Zahl verdichtet.</summary>
    Collect,

    /// <summary>Alle Treffer als Liste in Regelreihenfolge.</summary>
    RuleOrder,

    /// <summary>Alle Treffer als Liste, sortiert nach Ausgabepriorität (<c>outputValues</c>).</summary>
    OutputOrder
}
