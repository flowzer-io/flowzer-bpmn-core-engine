using Model;
using StorageSystem;

namespace WebApiEngine.Auth;

/// <summary>
/// Ein einmal gegen einen atomaren Directory-Snapshot aufgeloester Benutzer. Alle
/// Directory-basierten Rechte verwenden damit dieselbe exakte Issuer-/Subject-Regel und
/// dieselben aktiven Mitgliedschaften; Anzeigenamen und Token-Gruppen sind kein Ersatz.
/// </summary>
public sealed class DirectoryIdentityAccess
{
    private readonly Guid _userId;
    private readonly IReadOnlySet<Guid> _groupIds;

    private DirectoryIdentityAccess(Guid userId, IReadOnlySet<Guid> groupIds)
    {
        _userId = userId;
        _groupIds = groupIds;
    }

    public static DirectoryIdentityAccess? Resolve(CurrentUserContext currentUser, DirectorySnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        if (snapshot is null || currentUser.Identity is null
            || !string.Equals(snapshot.Issuer, currentUser.Identity.Issuer, StringComparison.Ordinal))
        {
            return null;
        }

        var matchingUsers = snapshot.Users.Where(user =>
                user.IsActive
                && string.Equals(user.Issuer, currentUser.Identity.Issuer, StringComparison.Ordinal)
                && string.Equals(user.Subject, currentUser.Identity.Subject, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matchingUsers.Length != 1) return null;

        var userId = matchingUsers[0].Id;
        var activeGroups = snapshot.Groups
            .Where(group => group.IsActive
                            && string.Equals(group.Issuer, snapshot.Issuer, StringComparison.Ordinal))
            .Select(group => group.Id)
            .ToHashSet();
        var memberships = snapshot.Memberships
            .Where(membership => membership.UserId == userId && activeGroups.Contains(membership.GroupId))
            .Select(membership => membership.GroupId)
            .ToHashSet();
        return new DirectoryIdentityAccess(userId, memberships);
    }

    public bool Matches(SubjectRef subject) => subject.Kind switch
    {
        DirectorySubjectKind.User => subject.Id == _userId,
        DirectorySubjectKind.Group => _groupIds.Contains(subject.Id),
        _ => false
    };
}
