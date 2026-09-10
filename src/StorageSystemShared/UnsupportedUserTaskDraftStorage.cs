namespace StorageSystem;

/// <summary>Kompatibilitaetsadapter fuer externe Storage-Implementierungen ohne Entwurfsablage.</summary>
internal sealed class UnsupportedUserTaskDraftStorage : IUserTaskDraftStorage
{
    internal static UnsupportedUserTaskDraftStorage Instance { get; } = new();

    private UnsupportedUserTaskDraftStorage() { }

    public Task<UserTaskDraft?> Get(Guid userTaskId, string ownerKey) => Unsupported<UserTaskDraft?>();

    public Task<UserTaskDraftWriteResult> TrySave(UserTaskDraft draft, long expectedRevision) =>
        Unsupported<UserTaskDraftWriteResult>();

    public Task<UserTaskDraftDeleteResult> TryDelete(Guid userTaskId, string ownerKey, long expectedRevision) =>
        Unsupported<UserTaskDraftDeleteResult>();

    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support user-task drafts."));
}
