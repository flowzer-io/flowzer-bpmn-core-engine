namespace StorageSystem;

/// <summary>Kompatibilitaetsadapter fuer externe Speicher ohne Autorenentwuerfe.</summary>
internal sealed class UnsupportedFormAuthoringStorage : IFormAuthoringStorage
{
    internal static UnsupportedFormAuthoringStorage Instance { get; } = new();
    private UnsupportedFormAuthoringStorage() { }

    public Task<FormAuthoringDraft?> Get(Guid formId) => Unsupported<FormAuthoringDraft?>();
    public Task<FormAuthoringWriteResult> TrySave(FormAuthoringDraft draft, long expectedRevision) =>
        Unsupported<FormAuthoringWriteResult>();
    public Task<FormAuthoringDeleteResult> TryDelete(Guid formId, long expectedRevision) =>
        Unsupported<FormAuthoringDeleteResult>();
    public Task<FormAuthoringPublishResult> TryPublish(
        Guid formId,
        long expectedRevision,
        Guid publishedFormId,
        string? publishedFormData = null) =>
        Unsupported<FormAuthoringPublishResult>();

    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support form-authoring drafts."));
}
