using Model;
using StorageSystem;

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
        if (subscription.AssignmentMode.HasValue)
        {
            return;
        }

        if (subscription.Token?.CurrentFlowNode is not BPMN.HumanInteraction.UserTask userTask)
        {
            return;
        }

        subscription.AssignmentMode = userTask.FlowzerAssignmentMode;
        if (userTask.FlowzerAssignmentMode == BPMN.HumanInteraction.UserTaskAssignmentMode.Directory)
        {
            // Historische Datensätze werden eindeutig aus dem unveränderlichen Tokenmodell
            // rekonstruiert. Eventuell vorhandener Freitext darf den Directory-Modus nie öffnen.
            subscription.Assignee = null;
            subscription.CandidateUsers = [];
            subscription.CandidateGroups = [];
            subscription.DirectoryAssigneeUserId = userTask.FlowzerDirectoryAssigneeUserId;
            subscription.DirectoryCandidateUserIds = [.. userTask.FlowzerDirectoryCandidateUserIds];
            subscription.DirectoryCandidateGroupIds = [.. userTask.FlowzerDirectoryCandidateGroupIds];
            return;
        }

        if (!string.IsNullOrWhiteSpace(subscription.Assignee)
            || subscription.CandidateUsers.Count > 0
            || subscription.CandidateGroups.Count > 0)
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
    /// Lädt den Snapshot höchstens einmal und nur dann, wenn mindestens eine der bereits
    /// materialisierten Aufgaben ihn tatsächlich benötigt. Fehlende Unterstützung bleibt
    /// für Textaufgaben folgenlos und lässt Directory-Aufgaben später geschlossen ausfallen.
    /// </summary>
    public static async Task<DirectorySnapshot?> LoadDirectorySnapshotIfRequiredAsync(
        IIdentityDirectoryStorage storage,
        IEnumerable<UserTaskSubscription> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(subscriptions);
        var requiresDirectory = false;
        foreach (var subscription in subscriptions)
        {
            EnsureAssignmentFromModel(subscription);
            requiresDirectory |= subscription.AssignmentMode == BPMN.HumanInteraction.UserTaskAssignmentMode.Directory;
        }

        if (!requiresDirectory) return null;
        try
        {
            return await storage.GetActiveSnapshot();
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sichtbar ist eine Aufgabe, wenn sie niemandem zugewiesen ist, die Person genannt ist,
    /// sie zu den Kandidaten gehoert, eine ihrer Gruppen genannt ist, oder sie den Betrieb
    /// verantwortet (<paramref name="seeAll"/>).
    /// </summary>
    public static bool IsVisibleTo(UserTaskSubscription subscription, UserTaskIdentity identity, bool seeAll)
    {
        EnsureAssignmentFromModel(subscription);
        if (seeAll)
        {
            return true;
        }

        // Dieser Legacy-Overload besitzt absichtlich keinen Directory-Kontext. Ein neuer
        // Aufrufpfad, der ihn versehentlich verwendet, soll geschlossen statt per Name öffnen.
        if (subscription.AssignmentMode == BPMN.HumanInteraction.UserTaskAssignmentMode.Directory)
        {
            return false;
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

        return MatchesAnyGroup(subscription.CandidateGroups, identity.Groups);
    }

    /// <summary>
    /// Einheitliche Laufzeitprüfung für beide Zuweisungsmodi. Im Directory-Modus zählen nur
    /// die verifizierte OIDC-Identität, stabile lokale IDs und aktuelle Mitgliedschaften.
    /// Anzeigenamen und Gruppen-Claims sind dort niemals ein Ersatz.
    /// </summary>
    public static bool IsVisibleTo(
        UserTaskSubscription subscription,
        CurrentUserContext currentUser,
        DirectorySnapshot? snapshot,
        bool seeAll)
    {
        EnsureAssignmentFromModel(subscription);
        if (seeAll) return true;

        if (subscription.AssignmentMode != BPMN.HumanInteraction.UserTaskAssignmentMode.Directory)
        {
            return IsVisibleTo(subscription, new UserTaskIdentity(currentUser.Names, currentUser.Groups), seeAll: false);
        }

        if (snapshot is null || currentUser.Identity is null
            || !string.Equals(snapshot.Issuer, currentUser.Identity.Issuer, StringComparison.Ordinal))
        {
            return false;
        }

        var matchingUsers = snapshot.Users.Where(user =>
                user.IsActive
                && string.Equals(user.Issuer, currentUser.Identity.Issuer, StringComparison.Ordinal)
                && string.Equals(user.Subject, currentUser.Identity.Subject, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matchingUsers.Length != 1)
        {
            return false;
        }

        var directoryUser = matchingUsers[0];
        if (subscription.DirectoryAssigneeUserId == directoryUser.Id
            || subscription.DirectoryCandidateUserIds.Contains(directoryUser.Id))
        {
            return true;
        }

        if (subscription.DirectoryCandidateGroupIds.Count == 0)
        {
            return false;
        }

        var activeGroups = snapshot.Groups
            .Where(group => group.IsActive
                            && string.Equals(group.Issuer, snapshot.Issuer, StringComparison.Ordinal))
            .Select(group => group.Id)
            .ToHashSet();
        return snapshot.Memberships.Any(membership =>
            membership.UserId == directoryUser.Id
            && activeGroups.Contains(membership.GroupId)
            && subscription.DirectoryCandidateGroupIds.Contains(membership.GroupId));
    }

    /// <summary>
    /// Trifft eine der genannten Kennungen auf eine der Kennungen der Person zu? Auch die
    /// Ordnerzuweisungen nennen Personen so, wie ein Modell es tut — deshalb oeffentlich.
    /// </summary>
    public static bool MatchesAnyName(IEnumerable<string> modelValues, IEnumerable<string> identityValues) =>
        MatchesAny(modelValues, identityValues);

    /// <summary>Trifft eine der genannten Gruppen auf eine Gruppe der Person zu?</summary>
    public static bool MatchesAnyGroup(IEnumerable<string> modelValues, IEnumerable<string> identityGroups)
    {
        var groups = identityGroups.ToArray();
        return modelValues.Any(candidate => !string.IsNullOrWhiteSpace(candidate)
                                            && groups.Any(group => IsSameGroup(candidate, group)));
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
