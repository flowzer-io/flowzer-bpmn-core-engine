namespace StorageSystem;

/// <summary>
/// Vereinheitlicht die fachlichen Publikationsregeln fuer Datei- und PostgreSQL-Ablage. Nur ein
/// vollstaendiger Snapshot darf diesen Schritt erreichen; fehlende Eintraege werden nicht
/// geloescht, sondern als historische, inaktive Identitaeten erhalten.
/// </summary>
public static class DirectorySnapshotPublisher
{
    public static DirectorySnapshot Merge(DirectorySnapshot? activeSnapshot, DirectorySnapshot importedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(importedSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(importedSnapshot.Issuer);
        ValidateIncomingSnapshot(importedSnapshot);

        var priorUsers = activeSnapshot?.Users ?? [];
        var priorGroups = activeSnapshot?.Groups ?? [];
        var usersByExternalIdentity = priorUsers.ToDictionary(UserIdentityKey.From);
        var groupsByExternalIdentity = priorGroups.ToDictionary(GroupIdentityKey.From);
        var incomingUserIds = new Dictionary<Guid, Guid>();
        var incomingGroupIds = new Dictionary<Guid, Guid>();

        var currentUsers = importedSnapshot.Users.Select(user => MergeUser(user, usersByExternalIdentity, incomingUserIds)).ToList();
        var currentGroups = importedSnapshot.Groups.Select(group => MergeGroup(group, importedSnapshot.Issuer, groupsByExternalIdentity, incomingGroupIds)).ToList();
        ResolveGroupParents(importedSnapshot.Groups, currentGroups, incomingGroupIds);

        var users = MergeHistoricalUsers(priorUsers, currentUsers);
        var groups = MergeHistoricalGroups(priorGroups, currentGroups);
        var memberships = MergeMemberships(importedSnapshot.Memberships, incomingUserIds, incomingGroupIds);

        return new DirectorySnapshot
        {
            GenerationId = importedSnapshot.GenerationId,
            Issuer = importedSnapshot.Issuer,
            CompletedAtUtc = importedSnapshot.CompletedAtUtc.ToUniversalTime(),
            Users = users,
            Groups = groups,
            Memberships = memberships
        };
    }

    public static DirectorySyncStatus CreateSucceededStatus(
        DirectorySnapshot snapshot,
        DirectorySyncStatus runningStatus) => new()
    {
        State = DirectorySyncState.Succeeded,
        Issuer = snapshot.Issuer,
        ActiveGenerationId = snapshot.GenerationId,
        AttemptedAtUtc = runningStatus.AttemptedAtUtc,
        SucceededAtUtc = snapshot.CompletedAtUtc.ToUniversalTime(),
        FailedAtUtc = runningStatus.FailedAtUtc,
        UserCount = snapshot.Users.Count(user => user.IsActive),
        GroupCount = snapshot.Groups.Count(group => group.IsActive),
        MembershipCount = snapshot.Memberships.Count
    };

    private static DirectoryUser MergeUser(
        DirectoryUser imported,
        IReadOnlyDictionary<UserIdentityKey, DirectoryUser> priorUsers,
        IDictionary<Guid, Guid> incomingUserIds)
    {
        var key = UserIdentityKey.From(imported);
        var id = priorUsers.TryGetValue(key, out var prior) ? prior.Id : Guid.NewGuid();
        MapIncomingId(incomingUserIds, imported.Id, id, "user");
        return new DirectoryUser
        {
            Id = id,
            SourceKind = imported.SourceKind,
            Issuer = imported.Issuer,
            Subject = imported.Subject,
            DisplayName = imported.DisplayName,
            IsActive = imported.IsActive
        };
    }

    private static DirectoryGroup MergeGroup(
        DirectoryGroup imported,
        string issuer,
        IReadOnlyDictionary<GroupIdentityKey, DirectoryGroup> priorGroups,
        IDictionary<Guid, Guid> incomingGroupIds)
    {
        var key = GroupIdentityKey.From(imported.SourceKind, issuer, imported.ExternalId);
        var id = priorGroups.TryGetValue(key, out var prior) ? prior.Id : Guid.NewGuid();
        MapIncomingId(incomingGroupIds, imported.Id, id, "group");
        return new DirectoryGroup
        {
            Id = id,
            SourceKind = imported.SourceKind,
            Issuer = issuer,
            ExternalId = imported.ExternalId,
            Name = imported.Name,
            Path = imported.Path,
            IsActive = imported.IsActive
        };
    }

    private static List<DirectoryUser> MergeHistoricalUsers(
        IEnumerable<DirectoryUser> previous,
        IEnumerable<DirectoryUser> current)
    {
        var currentKeys = current.Select(UserIdentityKey.From).ToHashSet();
        var histories = previous
            .Where(user => !currentKeys.Contains(UserIdentityKey.From(user)))
            .Select(user => new DirectoryUser
            {
                Id = user.Id, SourceKind = user.SourceKind, Issuer = user.Issuer, Subject = user.Subject,
                DisplayName = user.DisplayName, IsActive = false
            });
        return histories.Concat(current).OrderBy(user => user.Issuer, StringComparer.Ordinal)
            .ThenBy(user => user.Subject, StringComparer.Ordinal).ToList();
    }

    private static List<DirectoryGroup> MergeHistoricalGroups(
        IEnumerable<DirectoryGroup> previous,
        IEnumerable<DirectoryGroup> current)
    {
        var currentKeys = current.Select(GroupIdentityKey.From).ToHashSet();
        var histories = previous
            .Where(group => !currentKeys.Contains(GroupIdentityKey.From(group)))
            .Select(group => new DirectoryGroup
            {
                Id = group.Id, SourceKind = group.SourceKind, Issuer = group.Issuer, ExternalId = group.ExternalId,
                Name = group.Name, Path = group.Path, ParentId = group.ParentId,
                IsActive = false
            });
        return histories.Concat(current).OrderBy(group => group.Issuer, StringComparer.Ordinal)
            .ThenBy(group => group.Path, StringComparer.Ordinal).ToList();
    }

    private static void ResolveGroupParents(
        IReadOnlyList<DirectoryGroup> importedGroups,
        IReadOnlyList<DirectoryGroup> currentGroups,
        IReadOnlyDictionary<Guid, Guid> incomingGroupIds)
    {
        for (var index = 0; index < importedGroups.Count; index++)
        {
            var importedParentId = importedGroups[index].ParentId;
            if (importedParentId is null) continue;
            if (!incomingGroupIds.TryGetValue(importedParentId.Value, out var persistedParentId))
            {
                throw new ArgumentException("A directory group parent must refer to a group from the same snapshot.", nameof(importedGroups));
            }

            if (persistedParentId == currentGroups[index].Id)
            {
                throw new ArgumentException("A directory group cannot be its own parent.", nameof(importedGroups));
            }

            currentGroups[index].ParentId = persistedParentId;
        }
    }

    private static List<DirectoryMembership> MergeMemberships(
        IEnumerable<DirectoryMembership> importedMemberships,
        IReadOnlyDictionary<Guid, Guid> incomingUserIds,
        IReadOnlyDictionary<Guid, Guid> incomingGroupIds)
    {
        var memberships = new Dictionary<(Guid UserId, Guid GroupId), DirectoryMembership>();
        foreach (var membership in importedMemberships)
        {
            if (!incomingUserIds.TryGetValue(membership.UserId, out var userId)
                || !incomingGroupIds.TryGetValue(membership.GroupId, out var groupId))
            {
                throw new ArgumentException("A directory membership must refer to users and groups from the same snapshot.", nameof(importedMemberships));
            }

            memberships.TryAdd((userId, groupId), new DirectoryMembership { UserId = userId, GroupId = groupId });
        }

        return memberships.Values.OrderBy(membership => membership.UserId).ThenBy(membership => membership.GroupId).ToList();
    }

    private static void ValidateIncomingSnapshot(DirectorySnapshot snapshot)
    {
        if (snapshot.GenerationId == Guid.Empty) throw new ArgumentException("A directory snapshot requires a generation ID.", nameof(snapshot));
        if (snapshot.Users.Any(user => !string.Equals(user.Issuer, snapshot.Issuer, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Every directory user must belong to the snapshot issuer.", nameof(snapshot));
        }

        if (snapshot.Groups.Any(group => !string.Equals(group.Issuer, snapshot.Issuer, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Every directory group must belong to the snapshot issuer.", nameof(snapshot));
        }

        if (snapshot.Users.Any(user => user.Id == Guid.Empty || string.IsNullOrWhiteSpace(user.Subject)
                                       || string.IsNullOrWhiteSpace(user.DisplayName))
            || snapshot.Groups.Any(group => group.Id == Guid.Empty || string.IsNullOrWhiteSpace(group.ExternalId)
                                             || string.IsNullOrWhiteSpace(group.Name) || string.IsNullOrWhiteSpace(group.Path)))
        {
            throw new ArgumentException("Every directory identity requires complete stable fields and an import ID.", nameof(snapshot));
        }

        EnsureUnique(snapshot.Users.Select(UserIdentityKey.From), "user external identities");
        EnsureUnique(snapshot.Users.Select(user => user.Id), "user import IDs");
        EnsureUnique(snapshot.Groups.Select(group => GroupIdentityKey.From(group.SourceKind, snapshot.Issuer, group.ExternalId)), "group external identities");
        EnsureUnique(snapshot.Groups.Select(group => group.Id), "group import IDs");
        EnsureUnique(snapshot.Groups.Select(group => (group.SourceKind, Path: group.Path)), "group paths");
        ValidateGroupHierarchy(snapshot.Groups);
    }

    private static void ValidateGroupHierarchy(IReadOnlyCollection<DirectoryGroup> groups)
    {
        var byId = groups.ToDictionary(group => group.Id);
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();

        foreach (var group in groups)
        {
            Visit(group.Id);
        }

        void Visit(Guid groupId)
        {
            if (visited.Contains(groupId)) return;
            if (!visiting.Add(groupId))
            {
                throw new ArgumentException("A directory group hierarchy cannot contain a cycle.", nameof(groups));
            }

            var parentId = byId[groupId].ParentId;
            if (parentId.HasValue)
            {
                if (!byId.ContainsKey(parentId.Value))
                {
                    throw new ArgumentException("A directory group parent must refer to the same snapshot.", nameof(groups));
                }
                Visit(parentId.Value);
            }

            visiting.Remove(groupId);
            visited.Add(groupId);
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string description) where T : notnull
    {
        if (values.Distinct().Count() != values.Count())
        {
            throw new ArgumentException($"A directory snapshot contains duplicate {description}.");
        }
    }

    private static void MapIncomingId(IDictionary<Guid, Guid> mappings, Guid importedId, Guid persistedId, string subject)
    {
        if (importedId == Guid.Empty) return;
        if (!mappings.TryAdd(importedId, persistedId))
        {
            throw new ArgumentException($"A directory snapshot contains duplicate {subject} IDs.");
        }
    }

    private readonly record struct UserIdentityKey(DirectorySourceKind SourceKind, string Issuer, string Subject)
    {
        public static UserIdentityKey From(DirectoryUser user) => new(user.SourceKind, user.Issuer, user.Subject);
    }

    private readonly record struct GroupIdentityKey(DirectorySourceKind SourceKind, string Issuer, string ExternalId)
    {
        public static GroupIdentityKey From(DirectoryGroup group) => new(group.SourceKind, group.Issuer ?? string.Empty, group.ExternalId);
        public static GroupIdentityKey From(DirectorySourceKind sourceKind, string issuer, string externalId) => new(sourceKind, issuer, externalId);
    }
}
