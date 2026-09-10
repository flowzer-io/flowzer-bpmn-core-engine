using System.Collections.Concurrent;
using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>Einprozess-Entwicklungsablage für deduplizierte Task-Meldungen.</summary>
internal sealed class UserTaskNotificationStorage(Storage storage) : IUserTaskNotificationStorage
{
    internal const string DirectoryName = "UserTaskNotifications";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", DirectoryName));

    public async Task<UserTaskNotificationWriteResult> TryAdd(UserTaskNotification notification)
    {
        UserTaskNotificationContract.Validate(notification);
        var keyHash = Hash(notification.DeduplicationKey);
        var gate = Locks.GetOrAdd(keyHash, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var duplicate = await FindByDeduplicationKey(notification.DeduplicationKey);
            if (duplicate is not null) return new(false, duplicate);
            if (!TaskExists(notification.UserTaskId)) return new(false, null);
            await StorageFile.WriteAllTextAtomicAsync(NotificationFile(notification.Id),
                JsonConvert.SerializeObject(notification, storage.NewtonSoftDefaultSettings));
            return new(true, notification);
        }
        finally { gate.Release(); }
    }

    public async Task<UserTaskNotification?> Get(Guid notificationId)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(NotificationFile(notificationId));
        return content is null ? null
            : JsonConvert.DeserializeObject<UserTaskNotification>(content, storage.NewtonSoftDefaultSettings)
              ?? throw new InvalidDataException("Stored user-task notification is empty.");
    }

    public async Task<IReadOnlyList<UserTaskNotificationView>> GetForTasks(
        IEnumerable<Guid> userTaskIds, string ownerKey, DateTimeOffset? beforeUtc, int limit, bool unreadOnly)
    {
        UserTaskNotificationContract.ValidateOwner(ownerKey);
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var ids = userTaskIds.ToHashSet();
        var result = new List<UserTaskNotificationView>();
        foreach (var file in Directory.GetFiles(_path, "notification_*.json"))
        {
            var content = await System.IO.File.ReadAllTextAsync(file);
            var item = JsonConvert.DeserializeObject<UserTaskNotification>(content, storage.NewtonSoftDefaultSettings);
            if (item is null || !ids.Contains(item.UserTaskId)
                || beforeUtc is { } before && item.OccurredAtUtc >= before) continue;
            var read = await ReadAt(item.Id, ownerKey);
            if (!unreadOnly || read is null) result.Add(new(item, read));
        }
        return result.OrderByDescending(item => item.Notification.OccurredAtUtc)
            .ThenByDescending(item => item.Notification.Id).Take(limit).ToArray();
    }

    public async Task<bool> MarkRead(Guid notificationId, string ownerKey, DateTimeOffset readAtUtc)
    {
        UserTaskNotificationContract.ValidateOwner(ownerKey);
        var lockKey = $"read:{notificationId:N}:{ownerKey}";
        var gate = Locks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (await Get(notificationId) is null) return false;
            if (await ReadAt(notificationId, ownerKey) is not null) return true;
            await StorageFile.WriteAllTextAtomicAsync(ReadFile(notificationId, ownerKey),
                JsonConvert.SerializeObject(new UserTaskNotificationRead
                {
                    NotificationId = notificationId,
                    OwnerKey = ownerKey,
                    ReadAtUtc = readAtUtc
                }, storage.NewtonSoftDefaultSettings));
            return true;
        }
        finally { gate.Release(); }
    }

    internal void DeleteForTask(Guid taskId)
    {
        foreach (var file in Directory.GetFiles(_path, "notification_*.json"))
        {
            var content = StorageFile.ReadAllTextIfExists(file);
            var item = content is null ? null
                : JsonConvert.DeserializeObject<UserTaskNotification>(content, storage.NewtonSoftDefaultSettings);
            if (item?.UserTaskId != taskId) continue;
            StorageFile.DeleteIfExists(file);
            foreach (var read in Directory.GetFiles(_path, $"read_{item.Id:N}_*.json"))
                StorageFile.DeleteIfExists(read);
        }
    }

    private async Task<UserTaskNotification?> FindByDeduplicationKey(string key)
    {
        foreach (var file in Directory.GetFiles(_path, "notification_*.json"))
        {
            var content = await System.IO.File.ReadAllTextAsync(file);
            var item = JsonConvert.DeserializeObject<UserTaskNotification>(content, storage.NewtonSoftDefaultSettings);
            if (item?.DeduplicationKey == key) return item;
        }
        return null;
    }

    private async Task<DateTimeOffset?> ReadAt(Guid notificationId, string ownerKey)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(ReadFile(notificationId, ownerKey));
        return content is null ? null
            : JsonConvert.DeserializeObject<UserTaskNotificationRead>(content, storage.NewtonSoftDefaultSettings)?.ReadAtUtc;
    }

    private bool TaskExists(Guid taskId) => storage.SubscriptionStorage is MessageSubscriptionStorage subscriptions
                                            && subscriptions.UserTaskExists(taskId);
    private string NotificationFile(Guid id) => Path.Combine(_path, $"notification_{id:N}.json");
    private string ReadFile(Guid id, string owner) => Path.Combine(_path, $"read_{id:N}_{owner}.json");
    private static string Hash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
