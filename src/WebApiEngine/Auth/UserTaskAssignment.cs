using Model;

namespace WebApiEngine.Auth;

/// <summary>
/// Kennungen und Gruppen der aufrufenden Person, so wie ein BPMN-Modell sie nennen kann.
/// Welche davon der Identity Provider liefert, entscheidet dessen Konfiguration; die
/// Auswertung prueft deshalb alle.
/// </summary>
public sealed record UserTaskIdentity(
    IReadOnlyCollection<string> Names,
    IReadOnlyCollection<string> Groups);

/// <summary>
/// Wertet die Zuweisungen eines User-Tasks aus. Die Angaben standen bisher nur im Modell:
/// jede angemeldete Person sah jede Aufgabe.
/// </summary>
public static class UserTaskAssignment
{
    private static readonly char[] Separators = [',', ';'];

    /// <summary>
    /// Ergaenzt fehlende Zuweisungsfelder aus dem BPMN-Element im gespeicherten Token.
    ///
    /// Aufgaben, die vor der Einfuehrung dieser Felder entstanden sind, tragen sie nicht. Ohne
    /// diesen Nachzug blieben genau die laufenden Aufgaben fuer alle sichtbar, waehrend neue
    /// zugewiesen werden. Das Token enthaelt das Modellelement, deshalb braucht es keine
    /// Datenwanderung in der Ablage.
    /// </summary>
    public static void EnsureAssignmentFromModel(UserTaskSubscription subscription)
    {
        if (!string.IsNullOrWhiteSpace(subscription.Assignee)
            || subscription.CandidateUsers.Count > 0
            || subscription.CandidateGroups.Count > 0)
        {
            return;
        }

        if (subscription.Token?.CurrentFlowNode is not BPMN.HumanInteraction.UserTask userTask)
        {
            return;
        }

        subscription.Assignee = string.IsNullOrWhiteSpace(userTask.FlowzerAssignee) ? null : userTask.FlowzerAssignee.Trim();
        subscription.CandidateUsers = SplitList(userTask.FlowzerCandidateUsers);
        subscription.CandidateGroups = SplitList(userTask.FlowzerCandidateGroups);
    }

    /// <summary>Zerlegt eine Modellangabe wie <c>"bert, carla"</c> in Einzelwerte.</summary>
    public static List<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// Sichtbar ist eine Aufgabe, wenn sie niemandem zugewiesen ist, die Person genannt ist,
    /// sie zu den Kandidaten gehoert, eine ihrer Gruppen genannt ist, oder sie den Betrieb
    /// verantwortet (<paramref name="seeAll"/>).
    /// </summary>
    public static bool IsVisibleTo(UserTaskSubscription subscription, UserTaskIdentity identity, bool seeAll)
    {
        if (seeAll)
        {
            return true;
        }

        var hasAssignment = !string.IsNullOrWhiteSpace(subscription.Assignee)
                            || subscription.CandidateUsers.Count > 0
                            || subscription.CandidateGroups.Count > 0;

        if (!hasAssignment)
        {
            // Modelle ohne Zuweisung sind der bisherige Normalfall und bleiben offen.
            return true;
        }

        if (MatchesAny(subscription.Assignee is null ? [] : [subscription.Assignee], identity.Names))
        {
            return true;
        }

        if (MatchesAny(subscription.CandidateUsers, identity.Names))
        {
            return true;
        }

        return subscription.CandidateGroups.Any(candidate => identity.Groups.Any(group => IsSameGroup(candidate, group)));
    }

    private static bool MatchesAny(IEnumerable<string> modelValues, IEnumerable<string> identityValues)
    {
        var known = identityValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .ToHashSet(StringComparer.Ordinal);

        return modelValues.Any(value => !string.IsNullOrWhiteSpace(value) && known.Contains(Normalize(value)));
    }

    /// <summary>
    /// Keycloak liefert Gruppen als Pfad (<c>/abteilungen/buchhaltung</c>), Modelle nennen
    /// meist nur den Namen.
    ///
    /// Nennt das Modell einen Pfad, wird genau dieser Pfad verlangt: <c>/extern/buchhaltung</c>
    /// darf nicht auf <c>/intern/buchhaltung</c> passen. Nennt es nur einen Namen, zaehlt das
    /// letzte Glied des Pfades. Ein Teilstring reicht nie: <c>buchhaltung</c> passt nicht auf
    /// <c>buchhaltung-archiv</c>.
    /// </summary>
    private static bool IsSameGroup(string modelValue, string identityGroup)
    {
        var wanted = Normalize(modelValue);
        var actual = Normalize(identityGroup);

        if (wanted.Contains('/'))
        {
            return actual.TrimStart('/') == wanted.TrimStart('/');
        }

        return actual.TrimStart('/') == wanted
               || actual.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() == wanted;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
