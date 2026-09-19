namespace FlowzerDmn.Exceptions;

/// <summary>
/// Die Datei ist gueltiges DMN, benutzt aber eine Entscheidungslogik, die diese
/// Ausbaustufe nicht kennt — zum Beispiel <c>context</c>, <c>invocation</c> oder
/// <c>functionDefinition</c>.
/// </summary>
/// <remarks>
/// Der Fehler nennt das Element und die Decision, damit sich die Stelle in der Datei
/// ohne Suchen finden laesst.
/// </remarks>
public sealed class DmnUnsupportedException : DmnParseException
{
    /// <summary>Erzeugt den Fehler fuer ein nicht unterstuetztes Element einer Decision.</summary>
    /// <param name="elementName">Der lokale Name des Elements, zum Beispiel <c>invocation</c>.</param>
    /// <param name="decisionId">Die Id der Decision, in der das Element steht.</param>
    public DmnUnsupportedException(string elementName, string decisionId)
        : base($"Die Entscheidungslogik '{elementName}' der Decision '{decisionId}' wird nicht unterstuetzt. " +
               "Unterstuetzt sind 'decisionTable' und 'literalExpression'.")
    {
        ElementName = elementName;
        DecisionId = decisionId;
    }

    /// <summary>Der lokale Name des nicht unterstuetzten Elements.</summary>
    public string ElementName { get; }

    /// <summary>Die Id der betroffenen Decision.</summary>
    public string DecisionId { get; }
}
