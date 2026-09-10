using Model;
using StorageSystem;

namespace WebApiEngine.IdentityDirectory;

public enum DirectorySubjectSearchKind
{
    All = 0,
    User = 1,
    Group = 2
}

/// <summary>
/// Serverseitige Grenzen einer Identitätsauswahl. Die Oberfläche darf lediglich innerhalb
/// dieser Grenzen suchen; sie kann weder inaktive Einträge noch weitere Gruppen freischalten.
/// </summary>
public sealed class DirectorySubjectSelectionPolicy
{
    public bool AllowUsers { get; init; } = true;
    public bool AllowGroups { get; init; }
    public bool ActiveOnly { get; init; } = true;
    public IReadOnlySet<Guid>? AllowedUserIds { get; init; }
    public IReadOnlySet<Guid>? UserMemberOfGroupIds { get; init; }
    public IReadOnlySet<Guid>? AllowedGroupIds { get; init; }
    public bool IncludeSubgroups { get; init; }

    /// <summary>Modellierende dürfen aktive Benutzer und Gruppen des lokalen Bestands wählen.</summary>
    public static DirectorySubjectSelectionPolicy WorkflowModeling { get; } = new()
    {
        AllowUsers = true,
        AllowGroups = true,
        ActiveOnly = true,
        IncludeSubgroups = false
    };
}

public sealed record DirectorySubjectResult(
    SubjectRef Subject,
    string DisplayName,
    string Detail,
    bool IsActive = true,
    bool IsSelectable = true);

public sealed record DirectorySubjectSearchResult(
    Guid GenerationId,
    IReadOnlyList<DirectorySubjectResult> Items);

/// <summary>
/// Exakte Anzeigeprojektion bereits gespeicherter Referenzen. Sie ist absichtlich vom
/// Suchvertrag getrennt: ein historischer Treffer darf niemals wieder auswählbar werden.
/// </summary>
public sealed record DirectorySubjectResolutionResult(
    Guid GenerationId,
    IReadOnlyList<DirectorySubjectResult> Items);

/// <summary>
/// Sucht und prüft bekannte Identitäten ausschließlich gegen den aktuell atomar publizierten
/// Snapshot. Derselbe Kern kann später von Aufgaben, Formularen und Ordnerrechten verwendet
/// werden, sodass Browserfilter nie die serverseitige Auswahl erweitern.
/// </summary>
public sealed class DirectorySubjectSelectionService(IIdentityDirectoryStorage storage)
{
    public async Task<DirectorySubjectSearchResult?> SearchAsync(
        string query,
        DirectorySubjectSearchKind requestedKind,
        int limit,
        DirectorySubjectSelectionPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(policy);
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));

        var snapshot = await storage.GetActiveSnapshot();
        return snapshot is null ? null : Search(snapshot, query, requestedKind, limit, policy);
    }

    /// <summary>Fuehrt eine Suche auf genau einer bereits autorisierten Snapshot-Generation aus.</summary>
    public static DirectorySubjectSearchResult Search(
        DirectorySnapshot snapshot,
        string query,
        DirectorySubjectSearchKind requestedKind,
        int limit,
        DirectorySubjectSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(policy);
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));
        var items = EligibleSubjects(snapshot, requestedKind, policy)
            .Where(item => MatchesQuery(item, query.Trim()))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Detail, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Subject.Kind)
            .ThenBy(item => item.Subject.Id)
            .Take(limit)
            .ToArray();
        return new DirectorySubjectSearchResult(snapshot.GenerationId, items);
    }

    /// <summary>
    /// Prüft eine vom Client zurückgesendete Referenz erneut gegen den aktuellen Snapshot und
    /// die unveränderte Serverpolicy. Falsche Art und inaktive IDs fallen geschlossen aus.
    /// </summary>
    public async Task<DirectorySubjectResult?> ResolveAsync(
        SubjectRef subject,
        DirectorySubjectSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(policy);
        if (subject.Id == Guid.Empty) return null;

        var snapshot = await storage.GetActiveSnapshot();
        return snapshot is null
            ? null
            : Resolve(snapshot, subject, policy);
    }

    /// <summary>
    /// Löst eine kleine Menge bereits bekannter stabiler IDs zur Anzeige auf. Die Policy
    /// begrenzt die erlaubten Identitätsarten; <see cref="DirectorySubjectResult.IsSelectable"/>
    /// wird weiterhin mit der vollständigen aktuellen Auswahlpolicy berechnet.
    /// </summary>
    public async Task<DirectorySubjectResolutionResult?> ResolveForDisplayAsync(
        IReadOnlyCollection<SubjectRef> subjects,
        DirectorySubjectSelectionPolicy policy,
        IReadOnlySet<SubjectRef> contextSubjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(contextSubjects);
        if (subjects.Count is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(subjects));

        var snapshot = await storage.GetActiveSnapshot();
        return snapshot is null ? null : ResolveForDisplay(snapshot, subjects, policy, contextSubjects);
    }

    /// <summary>
    /// Snapshotreiner Kern für kontextgebundene Anzeigeauflösungen. Filter wie heutige
    /// Gruppenmitgliedschaft werden nur für <c>IsSelectable</c> ausgewertet: sonst würde
    /// eine nachträgliche Deaktivierung die historische Beschriftung zerstören.
    /// </summary>
    public static DirectorySubjectResolutionResult ResolveForDisplay(
        DirectorySnapshot snapshot,
        IReadOnlyCollection<SubjectRef> subjects,
        DirectorySubjectSelectionPolicy policy,
        IReadOnlySet<SubjectRef> contextSubjects)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(contextSubjects);
        if (subjects.Count is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(subjects));

        var items = subjects
            .Distinct()
            // Eine UUID allein ist kein Leserecht. Nur eine bereits im fachlichen Kontext
            // gespeicherte Referenz darf auch dann beschriftet werden, wenn sie inzwischen
            // inaktiv oder durch eine geänderte Policy ausgeschlossen ist.
            .Where(contextSubjects.Contains)
            .Select(subject => ProjectForDisplay(snapshot, subject, policy))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
        return new DirectorySubjectResolutionResult(snapshot.GenerationId, items);
    }

    /// <summary>
    /// Gemeinsamer, snapshotreiner Pruefkern fuer Suche und Formular-Submission. Dadurch
    /// entscheidet derselbe Policycode ueber angezeigte und tatsaechlich akzeptierte Werte.
    /// </summary>
    public static DirectorySubjectResult? Resolve(
        DirectorySnapshot snapshot,
        SubjectRef subject,
        DirectorySubjectSelectionPolicy policy) =>
        EligibleSubjects(snapshot, DirectorySubjectSearchKind.All, policy)
            .SingleOrDefault(item => item.Subject == subject);

    private static IEnumerable<DirectorySubjectResult> EligibleSubjects(
        DirectorySnapshot snapshot,
        DirectorySubjectSearchKind requestedKind,
        DirectorySubjectSelectionPolicy policy)
    {
        if (requestedKind != DirectorySubjectSearchKind.Group && policy.AllowUsers)
        {
            var membershipGroups = policy.UserMemberOfGroupIds is null
                ? null
                : ExpandGroups(snapshot, policy.UserMemberOfGroupIds, policy.IncludeSubgroups);
            var usersRestricted = policy.AllowedUserIds is not null || membershipGroups is not null;
            var usersInAllowedGroups = membershipGroups is null
                ? new HashSet<Guid>()
                : snapshot.Memberships
                    .Where(membership => membershipGroups.Contains(membership.GroupId))
                    .Select(membership => membership.UserId)
                    .ToHashSet();

            foreach (var user in snapshot.Users.Where(user =>
                         (!policy.ActiveOnly || user.IsActive)
                         && (!usersRestricted
                             || policy.AllowedUserIds?.Contains(user.Id) == true
                             || usersInAllowedGroups.Contains(user.Id))))
            {
                yield return new DirectorySubjectResult(
                    new SubjectRef(DirectorySubjectKind.User, user.Id),
                    user.DisplayName,
                    user.Subject,
                    user.IsActive,
                    IsSelectable: true);
            }
        }

        if (requestedKind != DirectorySubjectSearchKind.User && policy.AllowGroups)
        {
            var allowedGroups = policy.AllowedGroupIds is null
                ? null
                : ExpandGroups(snapshot, policy.AllowedGroupIds, policy.IncludeSubgroups);
            foreach (var group in snapshot.Groups.Where(group =>
                         (!policy.ActiveOnly || group.IsActive)
                         && (allowedGroups is null || allowedGroups.Contains(group.Id))))
            {
                yield return new DirectorySubjectResult(
                    new SubjectRef(DirectorySubjectKind.Group, group.Id),
                    group.Name,
                    group.Path,
                    group.IsActive,
                    IsSelectable: true);
            }
        }
    }

    private static DirectorySubjectResult? ProjectForDisplay(
        DirectorySnapshot snapshot,
        SubjectRef subject,
        DirectorySubjectSelectionPolicy policy)
    {
        if (subject.Id == Guid.Empty) return null;

        DirectorySubjectResult? result = subject.Kind switch
        {
            DirectorySubjectKind.User when policy.AllowUsers => snapshot.Users
                .Where(user => user.Id == subject.Id)
                .Select(user => new DirectorySubjectResult(
                    subject, user.DisplayName, user.Subject, user.IsActive, IsSelectable: false))
                .SingleOrDefault(),
            DirectorySubjectKind.Group when policy.AllowGroups => snapshot.Groups
                .Where(group => group.Id == subject.Id)
                .Select(group => new DirectorySubjectResult(
                    subject, group.Name, group.Path, group.IsActive, IsSelectable: false))
                .SingleOrDefault(),
            _ => null
        };
        return result is null
            ? null
            : result with { IsSelectable = Resolve(snapshot, subject, policy) is not null };
    }

    private static HashSet<Guid> ExpandGroups(
        DirectorySnapshot snapshot,
        IReadOnlySet<Guid> configuredIds,
        bool includeSubgroups)
    {
        var selected = configuredIds.ToHashSet();
        if (!includeSubgroups) return selected;

        var childrenByParent = snapshot.Groups
            .Where(group => group.ParentId.HasValue)
            .GroupBy(group => group.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Id).ToArray());
        var pending = new Queue<Guid>(selected);
        while (pending.TryDequeue(out var current))
        {
            if (!childrenByParent.TryGetValue(current, out var children)) continue;
            foreach (var child in children.Where(selected.Add)) pending.Enqueue(child);
        }

        return selected;
    }

    private static bool MatchesQuery(DirectorySubjectResult item, string query) =>
        item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Detail.Contains(query, StringComparison.OrdinalIgnoreCase)
        // Bereits veröffentlichte BPMN-Verträge speichern absichtlich nur diese lokale ID.
        // Der workflowgebundene Suchpfad darf sie deshalb zur Anzeigeauflösung akzeptieren.
        || item.Subject.Id.ToString().Equals(query, StringComparison.OrdinalIgnoreCase);
}
