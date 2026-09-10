namespace StorageSystem;

/// <summary>Atomarer Compare-and-swap-Vertrag fuer private Aufgabenentwuerfe.</summary>
public interface IUserTaskDraftStorage
{
    Task<UserTaskDraft?> Get(Guid userTaskId, string ownerKey);

    /// <summary>
    /// Schreibt nur, wenn die aktuelle Revision <paramref name="expectedRevision"/> entspricht.
    /// Revision 0 bezeichnet einen noch nicht vorhandenen Entwurf.
    /// </summary>
    Task<UserTaskDraftWriteResult> TrySave(UserTaskDraft draft, long expectedRevision);

    /// <summary>Loescht nur bei passender Revision; ein fehlender Entwurf entspricht Revision 0.</summary>
    Task<UserTaskDraftDeleteResult> TryDelete(Guid userTaskId, string ownerKey, long expectedRevision);
}

public enum UserTaskDraftWriteStatus
{
    Written,
    RevisionConflict,
    TaskNotFound
}

public sealed record UserTaskDraftWriteResult(
    UserTaskDraftWriteStatus Status,
    UserTaskDraft? Draft,
    long CurrentRevision);

public enum UserTaskDraftDeleteStatus
{
    Deleted,
    RevisionConflict,
    TaskNotFound
}

public sealed record UserTaskDraftDeleteResult(
    UserTaskDraftDeleteStatus Status,
    long CurrentRevision);
