using Model;
using StorageSystem;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Version = Model.Version;

namespace FilesystemStorageSystem;

public class FormStorage : IFormStorage
{
    internal static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SaveLocks = new();
    private readonly string _basePath;
    private readonly string _metaPath;
    private readonly string _foldersPath;
    private readonly Storage _storage;

    public FormStorage(Storage storage)
    {
        _storage = storage;
        _basePath = storage.GetBasePath("FileStorage/Forms");
        _metaPath = storage.GetBasePath("FileStorage/Forms/Meta");
        _foldersPath = storage.GetBasePath("FileStorage/Forms/Folders");
        EnsureDirectoryCreated();
        MigrateLegacySections();
    }

    private void EnsureDirectoryCreated()
    {
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);
        if (!Directory.Exists(_metaPath))
            Directory.CreateDirectory(_metaPath);
        if (!Directory.Exists(_foldersPath))
            Directory.CreateDirectory(_foldersPath);
    }

    private string GetMetaFilePath(Guid formId)
    {
        return Path.Combine(_metaPath, $"{formId}.json");
    }

    private string GetFormSearchPattern(Guid formId)
    {
        return $"{formId}_*.json";
    }

    private string? GetFormFilePath(Guid id)
    {
        return Directory.GetFiles(_basePath, $"*_{id}.json").SingleOrDefault();
    }

    public async Task SaveFormMetaData(FormMetadata formMetadata)
    {
        EnsureDirectoryCreated();
        var fullFileName = GetMetaFilePath(formMetadata.FormId);
        var data = SafeStorageJson.Serialize(formMetadata);
        await StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task<FormMetadata> GetFormMetaData(Guid formId)
    {
        EnsureDirectoryCreated();
        var fullFileName = GetMetaFilePath(formId);
        var data = File.ReadAllText(fullFileName);
        return Task.FromResult(SafeStorageJson.Deserialize<FormMetadata>(data));
    }

    public Task<IEnumerable<FormMetadata>> GetFormMetadatas()
    {
        EnsureDirectoryCreated();

        var metadatas = StorageFile.ReadExistingFiles(_metaPath, "*.json")
            .Select(entry => SafeStorageJson.Deserialize<FormMetadata>(entry.Content))
            .ToList();

        return Task.FromResult<IEnumerable<FormMetadata>>(metadatas);
    }

    public async Task UpdateFormMetaData(FormMetadata formMetaData)
    {
        EnsureDirectoryCreated();

        var metadataPath = GetMetaFilePath(formMetaData.FormId);
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException($"Form metadata not found with id: {formMetaData.FormId}", metadataPath);

        await SaveFormMetaData(formMetaData);
    }

    public Task DeleteFormMetaData(Guid formId)
    {
        EnsureDirectoryCreated();

        var metadataPath = GetMetaFilePath(formId);
        if (File.Exists(metadataPath))
            File.Delete(metadataPath);

        // Zu einer Form gehörende Versionen werden gemeinsam mit dem Metadatensatz entfernt,
        // damit kein verwaister Formularbestand im Dateisystem liegen bleibt.
        foreach (var file in Directory.GetFiles(_basePath, GetFormSearchPattern(formId)))
        {
            File.Delete(file);
        }

        if (_storage.FormAuthoringStorage is FormAuthoringStorage authoringStorage)
            authoringStorage.DeleteForForm(formId);

        return Task.CompletedTask;
    }

    public async Task SaveForm(Form form)
    {
        EnsureDirectoryCreated();
        var gate = SaveLocks.GetOrAdd(form.FormId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var versions = (await GetForms(form.FormId)).ToList();
            if (versions.Any(existing => existing.Id == form.Id || existing.Version.Equals(form.Version)))
                throw new StorageSystem.Exceptions.DefinitionStorageConflictException(
                    $"Published form '{form.FormId}' already contains ID '{form.Id}' or version {form.Version}.");

            var fullFileName = Path.Combine(_basePath, $"{form.FormId}_{form.Id}.json");
            var data = SafeStorageJson.Serialize(form);
            await StorageFile.WriteAllTextNewAtomicAsync(fullFileName, data);
        }
        catch (IOException)
        {
            throw new StorageSystem.Exceptions.DefinitionStorageConflictException(
                $"Published form '{form.Id}' is immutable and already exists.");
        }
        finally { gate.Release(); }
    }

    public Task<Form> GetForm(Guid id)
    {
        EnsureDirectoryCreated();
        var fullFileName = GetFormFilePath(id);
        if (string.IsNullOrEmpty(fullFileName))
            throw new FileNotFoundException("Form not found with id: " + id);

        var data = File.ReadAllText(fullFileName);
        return Task.FromResult(SafeStorageJson.Deserialize<Form>(data));
    }

    public Task<IEnumerable<Form>> GetForms(Guid formId)
    {
        EnsureDirectoryCreated();
        var forms = StorageFile.ReadExistingFiles(_basePath, GetFormSearchPattern(formId))
            .Select(entry => SafeStorageJson.Deserialize<Form>(entry.Content))
            .ToList();

        return Task.FromResult<IEnumerable<Form>>(forms);
    }

    public Task DeleteForm(Guid id)
    {
        EnsureDirectoryCreated();

        var fullFileName = GetFormFilePath(id);
        if (!string.IsNullOrEmpty(fullFileName) && File.Exists(fullFileName))
            File.Delete(fullFileName);

        return Task.CompletedTask;
    }

    public async Task<Version> GetMaxVersion(Guid formId)
    {
        // Für den ersten Speichervorgang wird bewusst auf 0.0 zurückgefallen;
        // die Business-Logik erhöht anschließend auf die erste fachliche Version.
        var forms = (await GetForms(formId)).ToList();
        if (forms.Count == 0)
            return new Version();

        return forms.Max(x => x.Version) ?? new Version();
    }

    public Task<IReadOnlyList<FormFolder>> GetFolders()
    {
        EnsureDirectoryCreated();
        IReadOnlyList<FormFolder> folders = StorageFile.ReadExistingFiles(_foldersPath, "*.json")
            .Select(entry => SafeStorageJson.Deserialize<FormFolder>(entry.Content))
            .OrderBy(folder => folder.Name, StringComparer.Ordinal)
            .ThenBy(folder => folder.Id)
            .ToArray();
        return Task.FromResult(folders);
    }

    public async Task<FormFolder?> GetFolder(Guid folderId)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(FolderFile(folderId));
        return content is null ? null : SafeStorageJson.Deserialize<FormFolder>(content);
    }

    public async Task SaveFolder(FormFolder folder)
    {
        try
        {
            await StorageFile.WriteAllTextNewAtomicAsync(FolderFile(folder.Id), SafeStorageJson.Serialize(folder));
        }
        catch (IOException)
        {
            throw new StorageSystem.Exceptions.DefinitionStorageConflictException(
                $"Form folder '{folder.Id}' already exists.");
        }
    }

    public async Task UpdateFolder(FormFolder folder)
    {
        var path = FolderFile(folder.Id);
        if (!File.Exists(path)) throw new FileNotFoundException($"Form folder not found with id: {folder.Id}", path);
        await StorageFile.WriteAllTextAtomicAsync(path, SafeStorageJson.Serialize(folder));
    }

    public Task DeleteFolder(Guid folderId)
    {
        var path = FolderFile(folderId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string FolderFile(Guid folderId) => Path.Combine(_foldersPath, $"{folderId:N}.json");

    /// <summary>
    /// Dateiablage ist ein Entwicklungsmodus ohne schema-basierte Migrationen. Daher wird der
    /// bisherige Abschnittsbestand beim Öffnen idempotent in reguläre Formulare kopiert. Die
    /// Quelldateien bleiben für veröffentlichte Alt-Referenzen bestehen.
    /// </summary>
    private void MigrateLegacySections()
    {
        var legacyRoot = _storage.GetBasePath("FileStorage/FormSections");
        var legacyMetadata = Path.Combine(legacyRoot, "Metadata");
        if (!Directory.Exists(legacyMetadata)) return;

        foreach (var entry in StorageFile.ReadExistingFiles(legacyMetadata, "*.json"))
        {
            var section = JsonConvert.DeserializeObject<FormSectionMetadata>(
                entry.Content, _storage.NewtonSoftDefaultSettings);
            if (section is null || File.Exists(GetMetaFilePath(section.SectionId))) continue;
            StorageFile.WriteAllTextAtomicAsync(
                GetMetaFilePath(section.SectionId),
                SafeStorageJson.Serialize(new FormMetadata { FormId = section.SectionId, Name = section.Name }))
                .GetAwaiter().GetResult();
        }

        foreach (var entry in StorageFile.ReadExistingFiles(legacyRoot, "*_*.json"))
        {
            var section = JsonConvert.DeserializeObject<FormSectionVersion>(
                entry.Content, _storage.NewtonSoftDefaultSettings);
            if (section is null) continue;
            var target = Path.Combine(_basePath, $"{section.SectionId}_{section.Id}.json");
            if (File.Exists(target)) continue;
            StorageFile.WriteAllTextAtomicAsync(target, SafeStorageJson.Serialize(new Form
            {
                Id = section.Id,
                FormId = section.SectionId,
                Version = section.Version,
                FormData = section.SectionData
            })).GetAwaiter().GetResult();
        }

        var legacyDrafts = _storage.GetBasePath("FileStorage/FormSectionAuthoringDrafts");
        var formDrafts = _storage.GetBasePath(Path.Combine("FileStorage", FormAuthoringStorage.DirectoryName));
        foreach (var entry in StorageFile.ReadExistingFiles(legacyDrafts, "draft_*.json"))
        {
            var section = JsonConvert.DeserializeObject<FormSectionAuthoringDraft>(
                entry.Content, _storage.NewtonSoftDefaultSettings);
            if (section is null) continue;
            var target = Path.Combine(formDrafts, $"draft_{section.SectionId:N}.json");
            if (File.Exists(target)) continue;
            StorageFile.WriteAllTextAtomicAsync(target, JsonConvert.SerializeObject(new FormAuthoringDraft
            {
                FormId = section.SectionId,
                Revision = section.Revision,
                UpdatedByUserId = section.UpdatedByUserId,
                UpdatedAtUtc = section.UpdatedAtUtc,
                BasedOnPublishedFormId = section.BasedOnPublishedSectionId,
                BasedOnVersion = section.BasedOnVersion,
                FormData = section.SectionData
            }, _storage.NewtonSoftDefaultSettings)).GetAwaiter().GetResult();
        }
    }
}
