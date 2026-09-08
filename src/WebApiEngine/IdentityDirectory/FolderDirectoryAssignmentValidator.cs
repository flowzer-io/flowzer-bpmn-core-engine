using Model;
using StorageSystem;

namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Prueft neue stabile Ordnerzuweisungen gegen genau einen aktiven Snapshot und setzt deren
/// Anzeigeprojektion serverseitig. Freitextzuweisungen bleiben davon vollstaendig getrennt.
/// </summary>
public static class FolderDirectoryAssignmentValidator
{
    public static void ValidateAndProject(
        IReadOnlyCollection<FolderAssignment> assignments,
        DirectorySnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        var directoryAssignments = assignments
            .Where(assignment => assignment.AssignmentMode == FolderAssignmentMode.Directory)
            .ToArray();
        if (directoryAssignments.Length == 0) return;
        if (snapshot is null)
            throw new InvalidOperationException("Kein erfolgreich synchronisierter Directory-Stand ist verfuegbar.");

        foreach (var assignment in directoryAssignments)
        {
            var subject = assignment.DirectorySubject
                ?? throw new ArgumentException("Eine Directory-Zuweisung braucht eine stabile Referenz.");
            var expectedKind = subject.Kind == DirectorySubjectKind.User
                ? FolderSubjectKind.User
                : FolderSubjectKind.Group;
            if (assignment.SubjectKind != expectedKind
                || !Guid.TryParse(assignment.Subject, out var compatibleId)
                || compatibleId != subject.Id)
            {
                throw new ArgumentException("Art, Kennung und Directory-Referenz einer Zuweisung muessen uebereinstimmen.");
            }

            var resolved = DirectorySubjectSelectionService.Resolve(
                snapshot,
                subject,
                DirectorySubjectSelectionPolicy.WorkflowModeling);
            if (resolved is null)
                throw new ArgumentException("Die Directory-Referenz ist unbekannt, inaktiv oder hat die falsche Art.");
            assignment.DisplayName = resolved.DisplayName;
        }
    }
}
