namespace FlowzerDmn.Exceptions;

/// <summary>
/// Ein Eingabewert liegt ausserhalb der <c>inputValues</c> seiner Spalte.
/// </summary>
/// <remarks>
/// Bewusste Abweichung von Camunda: Camunda wertet <c>inputValues</c> nur als Hinweis fuer
/// den Editor und rechnet stillschweigend weiter. Flowzer bricht ab, weil ein Wert
/// ausserhalb des erlaubten Bereichs sonst leise in die falsche Zeile faellt oder gar
/// nicht trifft — und das Ergebnis dann so aussieht, als haette die Tabelle nichts zu
/// sagen gehabt.
/// </remarks>
public sealed class DmnInputOutOfRangeException : DmnException
{
    /// <summary>Erzeugt den Fehler fuer einen Eingabewert ausserhalb des erlaubten Bereichs.</summary>
    /// <param name="decisionId">Die Id der betroffenen Decision.</param>
    /// <param name="inputId">Die Id der Eingabespalte, sofern vorhanden.</param>
    /// <param name="inputExpression">Der Eingabeausdruck der Spalte.</param>
    /// <param name="value">Der ermittelte Eingabewert.</param>
    /// <param name="allowedValues">Der Unary-Test aus <c>inputValues</c>.</param>
    public DmnInputOutOfRangeException(
        string decisionId,
        string? inputId,
        string inputExpression,
        object? value,
        string allowedValues)
        : base($"Der Eingabewert '{value ?? "null"}' der Spalte '{inputExpression}' " +
               $"(Decision '{decisionId}') liegt ausserhalb der erlaubten Werte: {allowedValues}.")
    {
        DecisionId = decisionId;
        InputId = inputId;
        InputExpression = inputExpression;
        Value = value;
        AllowedValues = allowedValues;
    }

    /// <summary>Die Id der betroffenen Decision.</summary>
    public string DecisionId { get; }

    /// <summary>Die Id der Eingabespalte, sofern die Datei eine mitbringt.</summary>
    public string? InputId { get; }

    /// <summary>Der Eingabeausdruck der Spalte.</summary>
    public string InputExpression { get; }

    /// <summary>Der ermittelte Eingabewert.</summary>
    public object? Value { get; }

    /// <summary>Der Unary-Test aus <c>inputValues</c>.</summary>
    public string AllowedValues { get; }
}
