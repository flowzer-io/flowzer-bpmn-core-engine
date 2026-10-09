using StorageSystem;

namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Installationsgrenze eines vollständigen aktiven Directory-Teilbaums. Historische
/// inaktive IDs bleiben lesbar, dürfen aber keinen Zugang oder neue Mitgliedschaft geben.
/// </summary>
internal static class DirectoryGroupScope
{
    internal static bool IsValidSnapshot(DirectorySnapshot? snapshot, string issuer, string rootExternalId)
    {
        if (snapshot is null || !string.Equals(snapshot.Issuer, issuer, StringComparison.Ordinal)) return false;
        var groups = snapshot.Groups.Where(group => group.IsActive).ToArray();
        if (groups.Any(group => group.Id == Guid.Empty || string.IsNullOrWhiteSpace(group.ExternalId)
                                || group.SourceKind != DirectorySourceKind.Keycloak
                                || !string.Equals(group.Issuer, issuer, StringComparison.Ordinal))
            || groups.Select(group => group.Id).Distinct().Count() != groups.Length
            || groups.Select(group => group.ExternalId).Distinct(StringComparer.Ordinal).Count() != groups.Length)
            return false;
        var roots = groups.Where(group => string.Equals(group.ExternalId, rootExternalId, StringComparison.Ordinal)).ToArray();
        if (roots.Length != 1 || roots[0].ParentId is not null) return false;

        var children = groups.Where(group => group.ParentId.HasValue).ToLookup(group => group.ParentId!.Value);
        var reachable = new HashSet<Guid>();
        var pending = new Queue<Guid>(); pending.Enqueue(roots[0].Id);
        while (pending.TryDequeue(out var id))
        {
            if (!reachable.Add(id)) return false;
            foreach (var child in children[id]) pending.Enqueue(child.Id);
        }
        // Ein alter realmweiter Stand, eine zweite Wurzel oder ein getrenntes
        // Zyklusfragment darf nicht als bereits eingerichteter Scope gelten.
        if (reachable.Count != groups.Length) return false;

        var users = snapshot.Users.Where(user => user.IsActive).ToArray();
        if (users.Any(user => user.Id == Guid.Empty || string.IsNullOrWhiteSpace(user.Subject)
                             || user.SourceKind != DirectorySourceKind.Keycloak
                             || !string.Equals(user.Issuer, issuer, StringComparison.Ordinal))
            || users.Select(user => user.Subject).Distinct(StringComparer.Ordinal).Count() != users.Length)
            return false;
        var userIds = snapshot.Users.Select(user => user.Id).ToHashSet();
        if (userIds.Count != snapshot.Users.Count) return false;
        var memberIds = new HashSet<Guid>();
        foreach (var membership in snapshot.Memberships)
        {
            if (!userIds.Contains(membership.UserId) || !reachable.Contains(membership.GroupId)) return false;
            memberIds.Add(membership.UserId);
        }
        return users.All(user => memberIds.Contains(user.Id));
    }

    internal static bool AllowsIdentity(DirectorySnapshot? snapshot, string issuer, string rootExternalId,
        string actorIssuer, string actorSubject) => string.Equals(actorIssuer, issuer, StringComparison.Ordinal)
        && IsValidSnapshot(snapshot, issuer, rootExternalId)
        && snapshot!.Users.Count(user => user.IsActive
            && string.Equals(user.Issuer, actorIssuer, StringComparison.Ordinal)
            && string.Equals(user.Subject, actorSubject, StringComparison.Ordinal)) == 1;
}
