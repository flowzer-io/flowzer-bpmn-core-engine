namespace StorageSystem;

/// <summary>
/// Persistenter CAS-Vertrag für tatsächliche Human-Task-Bearbeiter und deren Auditspur.
/// </summary>
public interface IUserTaskLifecycleStorage
{
    Task<UserTaskWorkState?> Get(Guid userTaskId);
    Task<IReadOnlyDictionary<Guid, UserTaskWorkState>> GetMany(IEnumerable<Guid> userTaskIds);

    /// <summary>
    /// Sperrt die Subscription in transaktionalen Ablagen gegen parallelen Abschluss oder
    /// Zustandswechsel. Ein fehlender Task wird geschlossen als <c>false</c> gemeldet.
    /// </summary>
    Task<bool> LockTask(Guid userTaskId);

    Task<UserTaskLifecycleWriteResult> TryWrite(
        UserTaskWorkState state,
        long expectedRevision,
        UserTaskAssignmentEvent auditEvent);

    Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEvents(Guid userTaskId);

    /// <summary>
    /// Liest die append-only Human-Task-Ereignisse eines Vorgangs. Die konkrete API-Projektion
    /// entscheidet anschließend, welche der darin enthaltenen Personen- und Betriebsdaten
    /// überhaupt sichtbar werden dürfen.
    /// </summary>
    Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEventsByProcessInstance(Guid processInstanceId);
}

public enum UserTaskLifecycleWriteStatus
{
    Written,
    RevisionConflict,
    TaskNotFound
}

public sealed record UserTaskLifecycleWriteResult(
    UserTaskLifecycleWriteStatus Status,
    UserTaskWorkState? State,
    long CurrentRevision);

/// <summary>Storage-unabhängige Invarianten für Zustand und Append-only-Ereignis.</summary>
public static class UserTaskLifecycleContract
{
    private static readonly HashSet<string> Actions =
        ["claim", "release", "assign", "delegate", "complete"];

    public static void Validate(
        UserTaskWorkState state,
        long expectedRevision,
        UserTaskAssignmentEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (expectedRevision < 0 || state.Revision != checked(expectedRevision + 1))
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (auditEvent.UserTaskId != state.UserTaskId || auditEvent.Revision != state.Revision)
            throw new ArgumentException("Audit event and work state must describe the same revision.", nameof(auditEvent));
        if (!Actions.Contains(auditEvent.Action)
            || string.IsNullOrWhiteSpace(auditEvent.Reason) || auditEvent.Reason.Length > 500
            || string.IsNullOrWhiteSpace(auditEvent.CorrelationId) || auditEvent.CorrelationId.Length > 128)
            throw new ArgumentException("The user-task audit event is invalid.", nameof(auditEvent));
        ValidateOwnerKey(auditEvent.ActorOwnerKey, nameof(auditEvent));

        if (state.AssigneeOwnerKey is null)
        {
            if (state.AssigneeUserId.HasValue || state.DirectoryAssigneeUserId.HasValue
                                               || state.AssigneeDisplayName is not null)
                throw new ArgumentException("An unassigned state must not contain assignee data.", nameof(state));
            return;
        }

        ValidateOwnerKey(state.AssigneeOwnerKey, nameof(state));
        if (state.AssigneeUserId.HasValue == state.DirectoryAssigneeUserId.HasValue)
            throw new ArgumentException("An assignee must have exactly one stable identity representation.", nameof(state));
    }

    private static void ValidateOwnerKey(string value, string parameterName)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A user-task owner key must be a SHA-256 hex value.", parameterName);
    }
}

/// <summary>Kompatibilitätsadapter für externe Ablagen ohne Human-Task-Lifecycle.</summary>
internal sealed class UnsupportedUserTaskLifecycleStorage : IUserTaskLifecycleStorage
{
    internal static UnsupportedUserTaskLifecycleStorage Instance { get; } = new();
    private UnsupportedUserTaskLifecycleStorage() { }
    public Task<UserTaskWorkState?> Get(Guid userTaskId) => Unsupported<UserTaskWorkState?>();
    public Task<IReadOnlyDictionary<Guid, UserTaskWorkState>> GetMany(IEnumerable<Guid> userTaskIds) =>
        Unsupported<IReadOnlyDictionary<Guid, UserTaskWorkState>>();
    public Task<bool> LockTask(Guid userTaskId) => Unsupported<bool>();
    public Task<UserTaskLifecycleWriteResult> TryWrite(UserTaskWorkState state, long expectedRevision, UserTaskAssignmentEvent auditEvent) =>
        Unsupported<UserTaskLifecycleWriteResult>();
    public Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEvents(Guid userTaskId) =>
        Unsupported<IReadOnlyList<UserTaskAssignmentEvent>>();
    public Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEventsByProcessInstance(Guid processInstanceId) =>
        Unsupported<IReadOnlyList<UserTaskAssignmentEvent>>();
    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support the user-task lifecycle."));
}
