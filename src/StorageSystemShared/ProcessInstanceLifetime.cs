namespace StorageSystem;

/// <summary>
/// Start- und Endzeitpunkt einer Instanz, abgeleitet aus ihren Tokens.
///
/// Die Ablage fuehrt bewusst keinen eigenen Instanzzeitstempel: <see cref="ProcessInstanceInfo"/>
/// kennt weder ein Start- noch ein Endefeld. Der letzte Zustandswechsel eines Tokens ist der
/// naechstliegende Ersatz — die Engine setzt <c>LastStateChangeTime</c> bei jedem Statuswechsel,
/// und der spaeteste davon ist der Moment, in dem die Instanz ihren Endzustand erreicht hat.
///
/// Die Aufbewahrung rechnet auf derselben Grundlage wie die Instanzansicht der Konsole. Beide
/// muessen dieselbe Zahl nennen: Eine Instanz, die in der Liste als „beendet am 1. Maerz" steht,
/// darf nicht nach einer anderen Frist verschwinden, als dort ablesbar ist.
/// </summary>
public static class ProcessInstanceLifetime
{
    /// <summary>Das aelteste Token markiert den Start; ohne Tokens gibt es keinen Startzeitpunkt.</summary>
    public static DateTime? GetStartedAtUtc(ProcessInstanceInfo instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.Tokens.Count == 0 ? null : instance.Tokens.Min(token => token.StartTime);
    }

    /// <summary>
    /// Der letzte Zustandswechsel einer beendeten Instanz markiert deren Ende. Eine laufende
    /// Instanz hat keinen Endzeitpunkt, und eine beendete ohne Tokens laesst sich nicht datieren
    /// — beides liefert <c>null</c>. Die Aufbewahrung laesst solche Instanzen bewusst stehen,
    /// statt sie mangels Datum sofort zu loeschen.
    /// </summary>
    public static DateTime? GetFinishedAtUtc(ProcessInstanceInfo instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!instance.IsFinished || instance.Tokens.Count == 0) return null;
        return instance.Tokens.Max(token => token.LastStateChangeTime);
    }
}
