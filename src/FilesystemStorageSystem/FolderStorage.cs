using Model;
using Newtonsoft.Json;
using StorageSystem;
using StorageSystem.Exceptions;

namespace FilesystemStorageSystem;

/// <summary>
/// Ordner als je eine JSON-Datei unterhalb von <c>FileStorage/Folders</c>. Der Dateiname ist die
/// Guid des Ordners; damit kann keine Kennung aus einer Adresse auf eine fremde Datei zeigen.
/// </summary>
public class FolderStorage : IFolderStorage
{
    private readonly string _basePath;
    private readonly Storage _storage;

    public FolderStorage(Storage storage)
    {
        _storage = storage;
        _basePath = storage.GetBasePath("FileStorage/Folders");
    }

    public Task<WorkflowFolder[]> GetAllFolders()
    {
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }

        var folders = StorageFile.ReadExistingFiles(_basePath, "*.json")
            .Select(entry => JsonConvert.DeserializeObject<WorkflowFolder>(entry.Content)!)
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(folders);
    }

    public async Task<WorkflowFolder?> GetFolder(Guid id)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(GetPath(id));
        return content is null ? null : JsonConvert.DeserializeObject<WorkflowFolder>(content);
    }

    public Task StoreFolder(WorkflowFolder folder)
    {
        var path = GetPath(folder.Id);
        if (File.Exists(path))
        {
            throw new DefinitionStorageConflictException($"Folder {folder.Id} already exists.");
        }

        return StorageFile.WriteAllTextAtomicAsync(path, Serialize(folder));
    }

    public Task UpdateFolder(WorkflowFolder folder)
    {
        var path = GetPath(folder.Id);
        if (!File.Exists(path))
        {
            throw new DefinitionStorageNotFoundException($"No folder found with id {folder.Id}");
        }

        return StorageFile.WriteAllTextAtomicAsync(path, Serialize(folder));
    }

    public Task DeleteFolder(Guid id)
    {
        var path = GetPath(id);
        if (!File.Exists(path))
        {
            throw new DefinitionStorageNotFoundException($"No folder found with id {id}");
        }

        File.Delete(path);
        return Task.CompletedTask;
    }

    private string Serialize(WorkflowFolder folder) =>
        JsonConvert.SerializeObject(folder, _storage.NewtonSoftDefaultSettings);

    private string GetPath(Guid id) => Path.Combine(_basePath, $"{id}.json");
}
