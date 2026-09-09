namespace StorageSystem;

/// <summary>Kompatibilitaetsadapter fuer Speicher ohne Formularabschnittsbibliothek.</summary>
internal sealed class UnsupportedFormSectionStorage : IFormSectionStorage
{
    internal static UnsupportedFormSectionStorage Instance { get; } = new();
    private UnsupportedFormSectionStorage() { }

    public Task CreateMetadata(FormSectionMetadata metadata) => Unsupported();
    public Task<FormSectionMetadata> GetMetadata(Guid sectionId) => Unsupported<FormSectionMetadata>();
    public Task<IReadOnlyList<FormSectionMetadata>> ListMetadata() => Unsupported<IReadOnlyList<FormSectionMetadata>>();
    public Task<FormSectionMetadata> RenameMetadata(Guid sectionId, string name) => Unsupported<FormSectionMetadata>();
    public Task<FormSectionVersion> GetVersion(Guid id) => Unsupported<FormSectionVersion>();
    public Task<FormSectionVersion> GetVersion(Guid sectionId, Model.Version version) =>
        Unsupported<FormSectionVersion>();
    public Task<IReadOnlyList<FormSectionVersion>> ListVersions(Guid sectionId) => Unsupported<IReadOnlyList<FormSectionVersion>>();
    public Task<FormSectionAuthoringDraft?> GetDraft(Guid sectionId) => Unsupported<FormSectionAuthoringDraft?>();
    public Task<FormSectionAuthoringWriteResult> TrySave(FormSectionAuthoringDraft draft, long expectedRevision) =>
        Unsupported<FormSectionAuthoringWriteResult>();
    public Task<FormSectionAuthoringDeleteResult> TryDelete(Guid sectionId, long expectedRevision) =>
        Unsupported<FormSectionAuthoringDeleteResult>();
    public Task<FormSectionAuthoringPublishResult> TryPublish(Guid sectionId, long expectedRevision, Guid publishedSectionId) =>
        Unsupported<FormSectionAuthoringPublishResult>();

    private static Task Unsupported() => Task.FromException(
        new NotSupportedException("This storage adapter does not support form sections."));
    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support form sections."));
}
