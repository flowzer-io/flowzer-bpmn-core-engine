using Model;
using StorageSystem;
using StorageSystem.Exceptions;

namespace WebApiEngine.Tests;

/// <summary>
/// Ordnerablage im Speicher fuer Testdoppel. Verhaelt sich wie die echten Implementierungen —
/// insbesondere bei Konflikt und Nichtgefunden —, damit ein Test, der auf 409 oder 404 prueft,
/// nicht nur die Attrappe prueft.
/// </summary>
internal sealed class InMemoryFolderStorage : IFolderStorage
{
    private readonly Dictionary<Guid, WorkflowFolder> _folders = [];

    public Task<WorkflowFolder[]> GetAllFolders() =>
        Task.FromResult(_folders.Values.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToArray());

    public Task<WorkflowFolder?> GetFolder(Guid id) =>
        Task.FromResult(_folders.TryGetValue(id, out var folder) ? folder : null);

    public Task StoreFolder(WorkflowFolder folder)
    {
        if (!_folders.TryAdd(folder.Id, folder))
        {
            throw new DefinitionStorageConflictException($"Folder {folder.Id} already exists.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateFolder(WorkflowFolder folder)
    {
        if (!_folders.ContainsKey(folder.Id))
        {
            throw new DefinitionStorageNotFoundException($"No folder found with id {folder.Id}");
        }

        _folders[folder.Id] = folder;
        return Task.CompletedTask;
    }

    public Task DeleteFolder(Guid id)
    {
        if (!_folders.Remove(id))
        {
            throw new DefinitionStorageNotFoundException($"No folder found with id {id}");
        }

        return Task.CompletedTask;
    }
}
