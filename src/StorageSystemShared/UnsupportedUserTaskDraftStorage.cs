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

    // Dieser Adapter erklaert ausdruecklich, dass er keine Entwuerfe fuehrt: Lesen und
    // Schreiben scheitern, also kann hier auch nie einer entstanden sein. Die Mengenauskuenfte
    // antworten deshalb wahrheitsgemaess mit "keine" statt zu scheitern — anders als bei einer
    // fremden Ablage, die Entwuerfe fuehren koennte und den Vertrag nur nicht kennt. Nur so
    // bleibt die Instanzmigration fuer diesen Adapter moeglich.
    public Task<int> CountForTask(Guid userTaskId) => Task.FromResult(0);

    public Task<int> DeleteAllForTask(Guid userTaskId) => Task.FromResult(0);

    public Task<int> RebindAllForTask(Guid userTaskId, Guid definitionId) => Task.FromResult(0);

    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support user-task drafts."));
}
