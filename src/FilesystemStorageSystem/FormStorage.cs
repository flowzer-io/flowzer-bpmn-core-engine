using Model;
using Newtonsoft.Json;
using StorageSystem;
using Version = Model.Version;

namespace FilesystemStorageSystem;

public class FormStorage : IFormStorage
{
    private readonly string _basePath;
    private readonly string _metaPath;
    private readonly Storage _storage;

    public FormStorage(Storage storage)
    {
        _storage = storage;
        _basePath = storage.GetBasePath("FileStorage/Forms");
        _metaPath = storage.GetBasePath("FileStorage/Forms/Meta");
        EnsureDirectoryCreated();
    }

    private void EnsureDirectoryCreated()
    {
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);
        if (!Directory.Exists(_metaPath))
            Directory.CreateDirectory(_metaPath);
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
        var data = JsonConvert.SerializeObject(formMetadata, _storage.NewtonSoftDefaultSettings);
        await File.WriteAllTextAsync(fullFileName, data);
    }

    public Task<FormMetadata> GetFormMetaData(Guid formId)
    {
        EnsureDirectoryCreated();
        var fullFileName = GetMetaFilePath(formId);
        var data = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<FormMetadata>(data, _storage.NewtonSoftDefaultSettings)!);
    }

    public Task<IEnumerable<FormMetadata>> GetFormMetadatas()
    {
        EnsureDirectoryCreated();

        var metadatas = Directory.GetFiles(_metaPath, "*.json")
            .Select(filePath =>
            {
                var data = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<FormMetadata>(data, _storage.NewtonSoftDefaultSettings)!;
            });

        return Task.FromResult(metadatas);
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

        return Task.CompletedTask;
    }

    public async Task SaveForm(Form form)
    {
        EnsureDirectoryCreated();
        var fullFileName = Path.Combine(_basePath, $"{form.FormId}_{form.Id}.json");
        var data = JsonConvert.SerializeObject(form, _storage.NewtonSoftDefaultSettings);
        await File.WriteAllTextAsync(fullFileName, data);
    }

    public Task<Form> GetForm(Guid id)
    {
        EnsureDirectoryCreated();
        var fullFileName = GetFormFilePath(id);
        if (string.IsNullOrEmpty(fullFileName))
            throw new FileNotFoundException("Form not found with id: " + id);

        var data = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<Form>(data, _storage.NewtonSoftDefaultSettings)!);
    }

    public Task<IEnumerable<Form>> GetForms(Guid formId)
    {
        EnsureDirectoryCreated();
        var forms = Directory.GetFiles(_basePath, GetFormSearchPattern(formId))
            .Select(filePath =>
            {
                var data = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<Form>(data, _storage.NewtonSoftDefaultSettings)!;
            });

        return Task.FromResult(forms);
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
        var maxVersion = (await GetForms(formId))
            .OrderByDescending(x => x.Version)
            .Select(x => x.Version)
            .FirstOrDefault();

        return maxVersion ?? new Version();
    }
}
