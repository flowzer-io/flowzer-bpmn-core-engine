using System.Collections.Concurrent;
using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>Einprozess-Entwicklungsablage für gebundene Human-Task-Termine.</summary>
internal sealed class UserTaskDeadlineStorage(Storage storage) : IUserTaskDeadlineStorage
{
    internal const string DirectoryName = "UserTaskDeadlines";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", DirectoryName));

    public async Task<UserTaskDeadline> AddIfAbsent(UserTaskDeadline deadline)
    {
        UserTaskDeadlineContract.Validate(deadline);
        var gate = Locks.GetOrAdd(deadline.UserTaskId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var existing = await Get(deadline.UserTaskId);
            if (existing is not null) return existing;
            if (!TaskExists(deadline.UserTaskId))
                throw new FileNotFoundException("The user task for the deadline does not exist.");
            await Write(deadline);
            return deadline;
        }
        finally { gate.Release(); }
    }

    public async Task<UserTaskDeadline?> Get(Guid userTaskId)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(File(userTaskId));
        return content is null
            ? null
            : JsonConvert.DeserializeObject<UserTaskDeadline>(content, storage.NewtonSoftDefaultSettings)
              ?? throw new InvalidDataException("Stored user-task deadline is empty.");
    }

    public async Task<IReadOnlyList<UserTaskDeadline>> GetDueCandidates(DateTimeOffset nowUtc, int limit)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        var result = new List<UserTaskDeadline>();
        foreach (var file in Directory.GetFiles(_path, "deadline_*.json"))
        {
            var content = await FileSystemRead(file);
            if (content is null) continue;
            var item = JsonConvert.DeserializeObject<UserTaskDeadline>(content, storage.NewtonSoftDefaultSettings);
            if (item?.NextCheckAtUtc is { } next && next <= nowUtc) result.Add(item);
        }
        return result.OrderBy(item => item.NextCheckAtUtc).Take(limit).ToArray();
    }

    public async Task<bool> TryAdvance(UserTaskDeadline deadline, long expectedRevision)
    {
        UserTaskDeadlineContract.Validate(deadline);
        var gate = Locks.GetOrAdd(deadline.UserTaskId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!TaskExists(deadline.UserTaskId)) return false;
            var current = await Get(deadline.UserTaskId);
            if (current?.Revision != expectedRevision || deadline.Revision != checked(expectedRevision + 1))
                return false;
            await Write(deadline);
            return true;
        }
        finally { gate.Release(); }
    }

    internal void Delete(Guid userTaskId) => StorageFile.DeleteIfExists(File(userTaskId));

    private bool TaskExists(Guid taskId) => storage.SubscriptionStorage is MessageSubscriptionStorage subscriptions
                                            && subscriptions.UserTaskExists(taskId);
    private Task Write(UserTaskDeadline item) => StorageFile.WriteAllTextAtomicAsync(File(item.UserTaskId),
        JsonConvert.SerializeObject(item, storage.NewtonSoftDefaultSettings));
    private string File(Guid taskId) => Path.Combine(_path, $"deadline_{taskId:N}.json");

    private static async Task<string?> FileSystemRead(string path)
    {
        try { return await System.IO.File.ReadAllTextAsync(path); }
        catch (FileNotFoundException) { return null; }
    }
}
