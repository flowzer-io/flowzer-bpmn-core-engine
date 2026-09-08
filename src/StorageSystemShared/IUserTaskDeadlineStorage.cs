namespace StorageSystem;

/// <summary>Persistiert einmalig gebundene Human-Task-Termine und deren Schedulerfortschritt.</summary>
public interface IUserTaskDeadlineStorage
{
    Task<UserTaskDeadline> AddIfAbsent(UserTaskDeadline deadline);
    Task<UserTaskDeadline?> Get(Guid userTaskId);
    Task<IReadOnlyList<UserTaskDeadline>> GetDueCandidates(DateTimeOffset nowUtc, int limit);
    Task<bool> TryAdvance(UserTaskDeadline deadline, long expectedRevision);
}

public static class UserTaskDeadlineContract
{
    private static readonly HashSet<string> ScheduleStates = ["none", "resolved", "unsupported", "invalid"];
    private static readonly HashSet<string> Statuses =
        ["none", "scheduled", "follow_up_due", "overdue", "escalated", "unsupported", "invalid"];

    public static void Validate(UserTaskDeadline item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.UserTaskId == Guid.Empty || item.Revision < 1
            || !ScheduleStates.Contains(item.ScheduleState) || !Statuses.Contains(item.Status)
            || string.IsNullOrWhiteSpace(item.PolicyVersion) || item.PolicyVersion.Length > 64
            || item.EmittedMilestones.Distinct(StringComparer.Ordinal).Count() != item.EmittedMilestones.Length)
            throw new ArgumentException("The user-task deadline is invalid.", nameof(item));
    }
}

/// <summary>Kompatibilitätsadapter für externe Ablagen ohne Deadline-Unterstützung.</summary>
internal sealed class UnsupportedUserTaskDeadlineStorage : IUserTaskDeadlineStorage
{
    internal static UnsupportedUserTaskDeadlineStorage Instance { get; } = new();
    private UnsupportedUserTaskDeadlineStorage() { }
    public Task<UserTaskDeadline> AddIfAbsent(UserTaskDeadline deadline) => Unsupported<UserTaskDeadline>();
    public Task<UserTaskDeadline?> Get(Guid userTaskId) => Unsupported<UserTaskDeadline?>();
    public Task<IReadOnlyList<UserTaskDeadline>> GetDueCandidates(DateTimeOffset nowUtc, int limit) =>
        Unsupported<IReadOnlyList<UserTaskDeadline>>();
    public Task<bool> TryAdvance(UserTaskDeadline deadline, long expectedRevision) => Unsupported<bool>();
    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support user-task deadlines."));
}
