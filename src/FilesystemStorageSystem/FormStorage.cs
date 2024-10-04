using Model;
using Newtonsoft.Json;
using StorageSystem;

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
    }

    public async Task SaveFormMetaData(FormMetadata formMetadata)
    {
        var fullFileName = Path.Combine(_metaPath, $"{formMetadata.FormId}.json");
        var data = JsonConvert.SerializeObject(formMetadata, _storage.NewtonSoftDefaultSettings);
        await File.WriteAllTextAsync(fullFileName, data);
    }

    public Task<FormMetadata> GetFormMetaData(Guid formId)
    {
        var fullFileName = Path.Combine(_metaPath, $"{formId}.json");
        var data = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<FormMetadata>(data, _storage.NewtonSoftDefaultSettings)!);
    }

    public async Task<IEnumerable<FormMetadata>> GetFormMetadatas()
    {

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
        var fullFileName = Path.Combine(_basePath, $"{form.FormId}.json");
        var data = JsonConvert.SerializeObject(form, _storage.NewtonSoftDefaultSettings);
        await File.WriteAllTextAsync(fullFileName, data);
    }

    public Task<Form> GetForm(Guid id)
    {
        var fullFileName = Path.Combine(_basePath, $"{id}.json");
        var data = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<Form>(data, _storage.NewtonSoftDefaultSettings)!);
    }

    public Task<IEnumerable<Form>> GetForms(Guid formId)
    {
        throw new NotImplementedException();
    }

    public Task UpdateForm(Form form)
    {
        throw new NotImplementedException();
    }

    public Task DeleteForm(Guid id)
    {
        throw new NotImplementedException();
    }
}