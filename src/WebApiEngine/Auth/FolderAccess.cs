using Model;

namespace WebApiEngine.Auth;

/// <summary>
/// Wer in welchem Ordner was darf. Reine Rechenlogik ueber einem bereits geladenen Ordnerbaum —
/// ohne Ablage, ohne HTTP —, damit sich die Vererbungsregel einzeln pruefen laesst.
///
/// Die Regel in einem Satz: Eine Zuweisung gilt fuer den Ordner, an dem sie haengt, und fuer
/// alles darunter; nach unten kann sie nur staerker werden, nie schwaecher.
/// </summary>
public static class FolderAccess
{
    /// <summary>
    /// Die staerkste Rolle je Ordner, Vererbung eingerechnet. Ordner ohne Rolle fehlen in der
    /// Abbildung — ein Eintrag bedeutet immer mindestens das Bearbeitungsrecht.
    /// </summary>
    public static IReadOnlyDictionary<Guid, FolderRole> ResolveRoles(
        IReadOnlyCollection<WorkflowFolder> folders,
        UserTaskIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(identity);

        var byId = folders.GroupBy(folder => folder.Id).ToDictionary(group => group.Key, group => group.First());
        var direct = byId.Values
            .Select(folder => (folder.Id, Role: StrongestDirectRole(folder, identity)))
            .Where(entry => entry.Role.HasValue)
            .ToDictionary(entry => entry.Id, entry => entry.Role!.Value);

        var effective = new Dictionary<Guid, FolderRole>();
        foreach (var folder in byId.Values)
        {
            FolderRole? role = null;
            foreach (var ancestor in WalkUp(folder, byId))
            {
                if (direct.TryGetValue(ancestor.Id, out var found))
                {
                    role = Stronger(role, found);
                }
            }

            if (role.HasValue)
            {
                effective[folder.Id] = role.Value;
            }
        }

        return effective;
    }

    /// <summary>
    /// Die geerbten Zuweisungen eines Ordners: alles, was an den uebergeordneten Ordnern haengt,
    /// mit dem Ordner, aus dem es stammt. Die Oberflaeche zeigt sie mit an — sonst wirkt eine
    /// Berechtigung, die man nirgends sieht, wie ein Fehler.
    /// </summary>
    public static IReadOnlyList<(WorkflowFolder Source, FolderAssignment Assignment)> CollectInherited(
        WorkflowFolder folder,
        IReadOnlyCollection<WorkflowFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(folders);

        var byId = folders.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.First());
        return WalkUp(folder, byId)
            .Skip(1)
            .SelectMany(ancestor => ancestor.Assignments.Select(assignment => (Source: ancestor, Assignment: assignment)))
            .ToList();
    }

    /// <summary>
    /// Der Pfad von der obersten Ebene bis zu diesem Ordner. Leer, wenn der Ordner unbekannt ist.
    /// </summary>
    public static IReadOnlyList<WorkflowFolder> BuildPath(Guid folderId, IReadOnlyCollection<WorkflowFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var byId = folders.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.First());
        if (!byId.TryGetValue(folderId, out var folder))
        {
            return [];
        }

        return WalkUp(folder, byId).Reverse().ToList();
    }

    /// <summary>
    /// Liegt <paramref name="candidateParentId"/> innerhalb von <paramref name="folderId"/>?
    /// Ein Ordner darf nicht unter sich selbst geschoben werden — der Ast waere danach vom Baum
    /// abgeschnitten und aus der Oberflaeche heraus nicht mehr erreichbar.
    /// </summary>
    public static bool WouldCreateCycle(Guid folderId, Guid? candidateParentId, IReadOnlyCollection<WorkflowFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        if (candidateParentId is not { } parentId)
        {
            return false;
        }

        if (parentId == folderId)
        {
            return true;
        }

        var byId = folders.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.First());
        return byId.TryGetValue(parentId, out var parent)
               && WalkUp(parent, byId).Any(ancestor => ancestor.Id == folderId);
    }

    /// <summary>
    /// Der Ordner selbst und seine Vorfahren, von unten nach oben. Bereits besuchte Ordner
    /// beenden den Lauf: Ein durch einen Fehler entstandener Ring darf die API nicht aufhaengen.
    /// </summary>
    private static IEnumerable<WorkflowFolder> WalkUp(WorkflowFolder folder, IReadOnlyDictionary<Guid, WorkflowFolder> byId)
    {
        var seen = new HashSet<Guid>();
        var current = folder;
        while (current is not null && seen.Add(current.Id))
        {
            yield return current;
            current = current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null;
        }
    }

    private static FolderRole? StrongestDirectRole(WorkflowFolder folder, UserTaskIdentity identity)
    {
        FolderRole? role = null;
        foreach (var assignment in folder.Assignments)
        {
            if (Matches(assignment, identity))
            {
                role = Stronger(role, assignment.Role);
            }
        }

        return role;
    }

    private static bool Matches(FolderAssignment assignment, UserTaskIdentity identity) =>
        assignment.SubjectKind switch
        {
            FolderSubjectKind.User => UserTaskAssignment.MatchesAnyName([assignment.Subject], identity.Names),
            FolderSubjectKind.Group => UserTaskAssignment.MatchesAnyGroup([assignment.Subject], identity.Groups),
            _ => false
        };

    /// <summary>Fachverantwortung schliesst das Bearbeiten ein und gewinnt deshalb immer.</summary>
    private static FolderRole Stronger(FolderRole? current, FolderRole candidate) =>
        current == FolderRole.Steward || candidate == FolderRole.Steward ? FolderRole.Steward : FolderRole.Editor;
}
