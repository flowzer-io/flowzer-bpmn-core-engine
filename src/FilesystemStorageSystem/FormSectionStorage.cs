using System.Collections.Concurrent;
using Model;
using Newtonsoft.Json;
using StorageSystem;
using StorageSystem.Exceptions;

namespace FilesystemStorageSystem;

/// <summary>
/// Entwicklungsablage fuer die Abschnittsbibliothek. Die Sperren gelten absichtlich nur im
/// aktuellen Prozess; mehrere API-Prozesse benötigen für dieselben CAS-Garantien PostgreSQL.
/// </summary>
internal sealed class FormSectionStorage(Storage storage) : IFormSectionStorage
{
    private const string SectionsDirectoryName = "FormSections";
    private const string DraftsDirectoryName = "FormSectionAuthoringDrafts";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SectionLocks = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> VersionIdLocks = new();
    private readonly string _sectionsPath = storage.GetBasePath(Path.Combine("FileStorage", SectionsDirectoryName));
    private readonly string _metadataPath = storage.GetBasePath(Path.Combine("FileStorage", SectionsDirectoryName, "Metadata"));
    private readonly string _draftsPath = storage.GetBasePath(Path.Combine("FileStorage", DraftsDirectoryName));

    public async Task CreateMetadata(FormSectionMetadata metadata)
    {
        ValidateMetadata(metadata);
        var gate = SectionLocks.GetOrAdd(metadata.SectionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await StorageFile.WriteAllTextNewAtomicAsync(
                MetadataFile(metadata.SectionId),
                JsonConvert.SerializeObject(metadata, storage.NewtonSoftDefaultSettings));
        }
        catch (IOException)
        {
            throw new DefinitionStorageConflictException(
                $"Form section metadata '{metadata.SectionId}' already exists.");
        }
        finally { gate.Release(); }
    }

    public async Task<FormSectionMetadata> GetMetadata(Guid sectionId)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(MetadataFile(sectionId));
        return content is null
            ? throw new FileNotFoundException($"Form section metadata not found with id: {sectionId}")
            : Deserialize<FormSectionMetadata>(content, "form section metadata");
    }

    public Task<IReadOnlyList<FormSectionMetadata>> ListMetadata()
    {
        IReadOnlyList<FormSectionMetadata> metadata = StorageFile
            .ReadExistingFiles(_metadataPath, "*.json")
            .Select(entry => Deserialize<FormSectionMetadata>(entry.Content, "form section metadata"))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.SectionId)
            .ToList();
        return Task.FromResult(metadata);
    }

    public async Task<FormSectionMetadata> RenameMetadata(Guid sectionId, string name)
    {
        ValidateSectionId(sectionId);
        ValidateName(name);
        var gate = SectionLocks.GetOrAdd(sectionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!MetadataExists(sectionId))
                throw new FileNotFoundException($"Form section metadata not found with id: {sectionId}");

            var renamed = new FormSectionMetadata(sectionId, name);
            await StorageFile.WriteAllTextAtomicAsync(
                MetadataFile(sectionId),
                JsonConvert.SerializeObject(renamed, storage.NewtonSoftDefaultSettings));
            return renamed;
        }
        finally { gate.Release(); }
    }

    public async Task<FormSectionVersion> GetVersion(Guid id)
    {
        ValidateSectionId(id);
        var path = Directory.GetFiles(_sectionsPath, $"*_{id:N}.json").SingleOrDefault();
        if (path is null)
            throw new FileNotFoundException($"Form section version not found with id: {id}");
        var content = await StorageFile.ReadAllTextIfExistsAsync(path);
        return content is null
            ? throw new FileNotFoundException($"Form section version not found with id: {id}")
            : Deserialize<FormSectionVersion>(content, "form section version");
    }

    public async Task<FormSectionVersion> GetVersion(Guid sectionId, Model.Version version)
    {
        ValidateSectionId(sectionId);
        ArgumentNullException.ThrowIfNull(version);
        var match = (await ListVersions(sectionId)).SingleOrDefault(candidate => candidate.Version == version);
        return match ?? throw new FileNotFoundException(
            $"Form section version not found with section id: {sectionId} and version: {version}");
    }

    public Task<IReadOnlyList<FormSectionVersion>> ListVersions(Guid sectionId)
    {
        ValidateSectionId(sectionId);
        IReadOnlyList<FormSectionVersion> versions = StorageFile
            .ReadExistingFiles(_sectionsPath, $"{sectionId:N}_*.json")
            .Select(entry => Deserialize<FormSectionVersion>(entry.Content, "form section version"))
            .OrderBy(entry => entry.Version)
            .ToList();
        return Task.FromResult(versions);
    }

    public async Task<FormSectionAuthoringDraft?> GetDraft(Guid sectionId)
    {
        ValidateSectionId(sectionId);
        var content = await StorageFile.ReadAllTextIfExistsAsync(DraftFile(sectionId));
        return content is null ? null : Deserialize<FormSectionAuthoringDraft>(content, "form section draft");
    }

    public async Task<FormSectionAuthoringWriteResult> TrySave(
        FormSectionAuthoringDraft draft,
        long expectedRevision)
    {
        ValidateDraft(draft, expectedRevision);
        var gate = SectionLocks.GetOrAdd(draft.SectionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!MetadataExists(draft.SectionId))
                return new FormSectionAuthoringWriteResult(
                    FormSectionAuthoringWriteStatus.SectionNotFound, null, 0);

            var current = await GetDraft(draft.SectionId);
            if ((current?.Revision ?? 0) != expectedRevision)
                return new FormSectionAuthoringWriteResult(
                    FormSectionAuthoringWriteStatus.RevisionConflict,
                    current,
                    current?.Revision ?? 0);

            await StorageFile.WriteAllTextAtomicAsync(
                DraftFile(draft.SectionId),
                JsonConvert.SerializeObject(draft, storage.NewtonSoftDefaultSettings));
            return new FormSectionAuthoringWriteResult(
                FormSectionAuthoringWriteStatus.Written, draft, draft.Revision);
        }
        finally { gate.Release(); }
    }

    public async Task<FormSectionAuthoringDeleteResult> TryDelete(Guid sectionId, long expectedRevision)
    {
        ValidateSectionId(sectionId);
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var gate = SectionLocks.GetOrAdd(sectionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!MetadataExists(sectionId))
                return new FormSectionAuthoringDeleteResult(
                    FormSectionAuthoringDeleteStatus.SectionNotFound, 0);

            var current = await GetDraft(sectionId);
            var currentRevision = current?.Revision ?? 0;
            if (currentRevision != expectedRevision)
                return new FormSectionAuthoringDeleteResult(
                    FormSectionAuthoringDeleteStatus.RevisionConflict, currentRevision);

            StorageFile.DeleteIfExists(DraftFile(sectionId));
            return new FormSectionAuthoringDeleteResult(FormSectionAuthoringDeleteStatus.Deleted, 0);
        }
        finally { gate.Release(); }
    }

    public async Task<FormSectionAuthoringPublishResult> TryPublish(
        Guid sectionId,
        long expectedRevision,
        Guid publishedSectionId)
    {
        ValidateSectionId(sectionId);
        if (expectedRevision <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (publishedSectionId == Guid.Empty)
            throw new ArgumentException("Published section ID is required.", nameof(publishedSectionId));

        var gate = SectionLocks.GetOrAdd(sectionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!MetadataExists(sectionId))
                return new FormSectionAuthoringPublishResult(
                    FormSectionAuthoringPublishStatus.SectionNotFound, null, 0);

            var draft = await GetDraft(sectionId);
            if (draft?.Revision != expectedRevision)
                return new FormSectionAuthoringPublishResult(
                    FormSectionAuthoringPublishStatus.RevisionConflict, null, draft?.Revision ?? 0);

            var existing = await ListVersions(sectionId);
            var nextVersion = (existing.Count == 0
                ? new Model.Version()
                : existing.Max(section => section.Version) ?? new Model.Version()) + 1;
            var published = new FormSectionVersion(
                publishedSectionId, sectionId, nextVersion, draft.SectionData);
            // Neben der Abschnittssperre schützt dieser Prozesslokal-Lock die globale
            // Versions-ID; die Datenbank erzwingt dieselbe Regel über ihren Primärschlüssel.
            var versionGate = VersionIdLocks.GetOrAdd(publishedSectionId, static _ => new SemaphoreSlim(1, 1));
            await versionGate.WaitAsync();
            try
            {
                if (VersionIdExists(publishedSectionId))
                    throw new DefinitionStorageConflictException(
                        $"Published form section version '{publishedSectionId}' already exists.");
                await StorageFile.WriteAllTextNewAtomicAsync(
                    VersionFile(sectionId, publishedSectionId),
                    JsonConvert.SerializeObject(published, storage.NewtonSoftDefaultSettings));
            }
            catch (IOException)
            {
                throw new DefinitionStorageConflictException(
                    $"Published form section '{sectionId}' already contains ID '{publishedSectionId}' or version {nextVersion}.");
            }
            finally { versionGate.Release(); }

            StorageFile.DeleteIfExists(DraftFile(sectionId));
            return new FormSectionAuthoringPublishResult(
                FormSectionAuthoringPublishStatus.Published, published, 0);
        }
        finally { gate.Release(); }
    }

    private bool MetadataExists(Guid sectionId) => File.Exists(MetadataFile(sectionId));
    private bool VersionIdExists(Guid id) => Directory.GetFiles(_sectionsPath, $"*_{id:N}.json").Length > 0;
    private string MetadataFile(Guid sectionId) => Path.Combine(_metadataPath, $"{sectionId:N}.json");
    private string DraftFile(Guid sectionId) => Path.Combine(_draftsPath, $"draft_{sectionId:N}.json");
    private string VersionFile(Guid sectionId, Guid id) => Path.Combine(_sectionsPath, $"{sectionId:N}_{id:N}.json");

    private T Deserialize<T>(string content, string entity) =>
        JsonConvert.DeserializeObject<T>(content, storage.NewtonSoftDefaultSettings)
        ?? throw new InvalidDataException($"Stored {entity} is empty.");

    private static void ValidateMetadata(FormSectionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateSectionId(metadata.SectionId);
        ValidateName(metadata.Name);
    }

    private static void ValidateDraft(FormSectionAuthoringDraft draft, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateSectionId(draft.SectionId);
        if (expectedRevision < 0 || draft.Revision != checked(expectedRevision + 1))
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (draft.UpdatedByUserId == Guid.Empty)
            throw new ArgumentException("Updating user ID is required.", nameof(draft));
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SectionData);
    }

    private static void ValidateSectionId(Guid sectionId)
    {
        if (sectionId == Guid.Empty)
            throw new ArgumentException("Form section ID is required.", nameof(sectionId));
    }

    private static void ValidateName(string name) => ArgumentException.ThrowIfNullOrWhiteSpace(name);
}
