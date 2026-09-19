namespace FlowzerDmn.Exceptions;

/// <summary>
/// Die DMN-Datei laesst sich nicht in ein auswertbares Modell uebersetzen: kaputtes XML,
/// fehlende Pflichtangaben, widerspruechliche Tabellen oder ein Zyklus im Entscheidungsnetz.
/// </summary>
public class DmnParseException : DmnException
{
    /// <inheritdoc cref="DmnException(string)"/>
    public DmnParseException(string message) : base(message)
    {
    }

    /// <inheritdoc cref="DmnException(string, Exception)"/>
    public DmnParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
