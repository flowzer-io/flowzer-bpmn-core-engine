namespace FlowzerDmn.Exceptions;

/// <summary>
/// Basis aller Fehler der DMN-Bibliothek. Wer DMN einbindet, kann daran alles abfangen,
/// was aus dem Parsen oder Auswerten kommt.
/// </summary>
public abstract class DmnException : Exception
{
    /// <inheritdoc cref="Exception(string)"/>
    protected DmnException(string message) : base(message)
    {
    }

    /// <inheritdoc cref="Exception(string, Exception)"/>
    protected DmnException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
