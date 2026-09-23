namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Fachliche Einzelfehler eines abgeschlossenen Timer-Ticks. Separater Typ, damit
/// der Hochlauf nur diese Fehler toleriert, nicht etwa fehlgeschlagene DB-Commits
/// oder eine beschädigte Wiederherstellung der Instanz-Subscriptions.
/// </summary>
internal sealed class TimerProcessingException(IEnumerable<Exception> failures)
    : AggregateException("One or more due timer subscriptions could not be processed.", failures);
