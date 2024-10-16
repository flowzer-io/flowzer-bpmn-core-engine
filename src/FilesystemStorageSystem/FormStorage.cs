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
        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);
    }

    public async Task SaveFormMetaData(FormMetadata formMetadata)
    {
        EnsureDirectoryCreated();
        var fullFileName = Path.Combine(_metaPath, $"{formMetadata.FormId}.json");
        var data = JsonConvert.SerializeObject(formMetadata, _storage.NewtonSoftDefaultSettings);
        await File.WriteAllTextAsync(fullFileName, data);
    }

    public Task<FormMetadata> GetFormMetaData(Guid formId)
    {
        EnsureDirectoryCreated();
        var fullFileName = Path.Combine(_metaPath, $"{formId}.json");
        var data = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<FormMetadata>(data, _storage.NewtonSoftDefaultSettings)!);
    }

    public async Task<IEnumerable<FormMetadata>> GetFormMetadatas()
    {

        EnsureDirectoryCreated();
        
        return Directory.GetFiles(_metaPath, "*.json")
            .Select(filePath =>
            {
                var data = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<FormMetadata>(data, _storage.NewtonSoftDefaultSettings)!;
            });

    }

    public Task UpdateFormMetaData(FormMetadata formMetaData)
    {
        throw new NotImplementedException();
    }

    public Task DeleteFormMetaData(Guid formId)
    {
        throw new NotImplementedException();
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
        var fullFileName = Directory.GetFiles(_basePath, $"*_{id}.json").SingleOrDefault();
        if (string.IsNullOrEmpty(fullFileName))
            throw new Exception("Form not found with id: " + id);
        
        var data = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<Form>(data, _storage.NewtonSoftDefaultSettings)!);
    }

    public async Task<IEnumerable<Form>> GetForms(Guid formId)
    {
        EnsureDirectoryCreated();
        return Directory.GetFiles(_basePath, $"{formId}_*.json")
            .Select(filePath =>
            {
                var data = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<Form>(data, _storage.NewtonSoftDefaultSettings)!;
            });
    }

    public Task DeleteForm(Guid id)
    {
        throw new NotImplementedException();
    }

    public async Task<Version> GetMaxVersion(Guid formId)
    {
        var maxVersion = (await GetForms(formId)).Max(x => x.Version);
        return maxVersion ?? new Version();
    }
}