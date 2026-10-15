using BPMN.HumanInteraction;
using WebApiEngine.Forms;

namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Friert den direkten Bearbeiter einmal beim Erzeugen einer neuen Aufgaben-Subscription
/// ein. Quelle und Eingaben verleihen keine Rechte: Ziel muss eindeutig im aktuellen
/// vollständigen Directory stehen; spätere Bearbeitung prüft weiterhin den aktuellen Stand.
/// </summary>
internal static class DirectoryTaskAssigneeResolver
{
    public static Guid Resolve(UserTask task, Token token, InstanceEngine engine, DirectorySnapshot? snapshot)
    {
        if (snapshot is null || task.FlowzerAssignmentMode != UserTaskAssignmentMode.Directory
            || !DirectoryAssigneeSource.IsValid(task.FlowzerDirectoryAssigneeSource)
            || task.FlowzerDirectoryAssigneeUserId is not null
            || task.FlowzerDirectoryCandidateUserIds.Count != 0 || task.FlowzerDirectoryCandidateGroupIds.Count != 0)
            throw Error();

        DirectoryUser? target;
        if (task.FlowzerDirectoryAssigneeSource == DirectoryAssigneeSource.Initiator)
        {
            var initiator = engine.MasterToken.Initiator;
            if (initiator is null || !string.Equals(initiator.Issuer, snapshot.Issuer, StringComparison.Ordinal)) throw Error();
            target = Unique(snapshot.Users.Where(user => user.Issuer == initiator.Issuer && user.Subject == initiator.Subject));
        }
        else
        {
            if (!DirectoryAssigneeSource.TryGetVariableName(task.FlowzerDirectoryAssigneeSource, out var name)) throw Error();
            // Eingangszuordnungen einer Task haben Vorrang. Fehlt dort der Schlüssel,
            // lesen wir genau einen Root-Schlüssel, nicht die freie Expression-Engine.
            object? value = null;
            var local = token.Variables as IDictionary<string, object?>;
            if (local?.TryGetValue(name, out value) != true)
            {
                var root = engine.GetProcessToken(token).Variables as IDictionary<string, object?>;
                if (root?.TryGetValue(name, out value) != true) throw Error();
            }
            if (!DirectorySubjectValue.TryParse(value, out var subject) || subject.Kind != DirectorySubjectKind.User) throw Error();
            target = Unique(snapshot.Users.Where(user => user.Id == subject.Id));
        }

        if (target is null || !target.IsActive || target.Id == Guid.Empty
            || target.SourceKind != DirectorySourceKind.Keycloak
            || string.IsNullOrWhiteSpace(target.Subject)
            || !string.Equals(target.Issuer, snapshot.Issuer, StringComparison.Ordinal)
            || snapshot.Users.Count(user => user.Id == target.Id) != 1
            || snapshot.Users.Count(user => user.Issuer == target.Issuer && user.Subject == target.Subject) != 1)
            throw Error();
        return target.Id;
    }

    private static DirectoryUser? Unique(IEnumerable<DirectoryUser> users)
    {
        var candidates = users.Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    // Keine eingereichten Formularwerte oder Identitätsdetails in Fehlern/Logs ausgeben.
    private static InvalidOperationException Error() => new("User task has an invalid or unavailable directory assignment source.");
}
