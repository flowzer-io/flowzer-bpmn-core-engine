namespace FlowzerDmn.Exceptions;

/// <summary>
/// Die Auswertung kommt nicht durch: unbekannte Decision-Id, eine Verdichtung ueber
/// Werte, die keine Zahlen sind, oder eine Abhaengigkeit, die ins Leere zeigt.
/// </summary>
public sealed class DmnEvaluationException : DmnException
{
    /// <inheritdoc cref="DmnException(string)"/>
    public DmnEvaluationException(string message) : base(message)
    {
    }

    /// <inheritdoc cref="DmnException(string, Exception)"/>
    public DmnEvaluationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
