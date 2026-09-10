using BPMN.HumanInteraction;
using StorageSystem;

namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Prüft beim Deployment, dass jede stabile Aufgabenreferenz im aktuell vollständig
/// veröffentlichten Verzeichnis existiert, aktiv ist und die erwartete Art besitzt.
/// </summary>
public static class DirectoryTaskAssignmentValidator
{
    public static void Validate(IEnumerable<UserTask> tasks, DirectorySnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var taskArray = tasks.ToArray();
        if (taskArray.All(task => task.FlowzerAssignmentMode == UserTaskAssignmentMode.Text))
        {
            foreach (var task in taskArray) ValidateTextTask(task);
            return;
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException(
                "A published identity directory snapshot is required for directory task assignments.");
        }

        var activeUsers = UniqueActiveIds(
            snapshot.Users.Where(user => string.Equals(user.Issuer, snapshot.Issuer, StringComparison.Ordinal)),
            user => user.Id);
        var activeGroups = UniqueActiveIds(
            snapshot.Groups.Where(group => string.Equals(group.Issuer, snapshot.Issuer, StringComparison.Ordinal)),
            group => group.Id);

        foreach (var task in taskArray)
        {
            switch (task.FlowzerAssignmentMode)
            {
                case UserTaskAssignmentMode.Text:
                    ValidateTextTask(task);
                    break;
                case UserTaskAssignmentMode.Directory:
                    ValidateDirectoryTask(task, activeUsers, activeGroups);
                    break;
                default:
                    throw AssignmentError(task, "assignment mode is unsupported");
            }
        }
    }

    private static void ValidateTextTask(UserTask task)
    {
        if (task.FlowzerDirectoryAssigneeUserId.HasValue
            || task.FlowzerDirectoryCandidateUserIds.Count > 0
            || task.FlowzerDirectoryCandidateGroupIds.Count > 0)
        {
            throw AssignmentError(task, "text mode cannot contain directory references");
        }
    }

    private static void ValidateDirectoryTask(
        UserTask task,
        IReadOnlySet<Guid> activeUsers,
        IReadOnlySet<Guid> activeGroups)
    {
        if (!string.IsNullOrWhiteSpace(task.FlowzerAssignee)
            || !string.IsNullOrWhiteSpace(task.FlowzerCandidateUsers)
            || !string.IsNullOrWhiteSpace(task.FlowzerCandidateGroups))
        {
            throw AssignmentError(task, "directory mode cannot contain text assignments");
        }

        var userIds = task.FlowzerDirectoryCandidateUserIds
            .Concat(task.FlowzerDirectoryAssigneeUserId is { } assignee ? [assignee] : [])
            .ToArray();
        var groupIds = task.FlowzerDirectoryCandidateGroupIds.ToArray();
        if (userIds.Length == 0 && groupIds.Length == 0)
        {
            throw AssignmentError(task, "directory mode requires at least one reference");
        }

        if (userIds.Any(id => id == Guid.Empty || !activeUsers.Contains(id)))
        {
            throw AssignmentError(task, "a user reference is unknown, inactive or has the wrong kind");
        }

        if (groupIds.Any(id => id == Guid.Empty || !activeGroups.Contains(id)))
        {
            throw AssignmentError(task, "a group reference is unknown, inactive or has the wrong kind");
        }

        if (userIds.Distinct().Count() != userIds.Length
            || groupIds.Distinct().Count() != groupIds.Length)
        {
            throw AssignmentError(task, "directory references must be unique");
        }
    }

    private static HashSet<Guid> UniqueActiveIds<T>(IEnumerable<T> entries, Func<T, Guid> id)
        where T : class
    {
        var active = entries.Where(entry => entry switch
            {
                DirectoryUser user => user.IsActive,
                DirectoryGroup group => group.IsActive,
                _ => false
            })
            .ToArray();
        var duplicateIds = active.GroupBy(id).Where(group => group.Count() != 1).Select(group => group.Key).ToHashSet();
        return active.Select(id).Where(entryId => entryId != Guid.Empty && !duplicateIds.Contains(entryId)).ToHashSet();
    }

    private static InvalidOperationException AssignmentError(UserTask task, string reason) =>
        new($"User task '{task.Id}' has an invalid directory assignment: {reason}.");
}
