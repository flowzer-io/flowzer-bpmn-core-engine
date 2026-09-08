namespace StorageSystem;

public sealed record UserTaskNotificationWriteResult(bool Added, UserTaskNotification? Notification);
public sealed record UserTaskNotificationView(UserTaskNotification Notification, DateTimeOffset? ReadAtUtc);

/// <summary>Persistente, deduplizierte Task-Meldungen samt benutzerspezifischem Lesestatus.</summary>
public interface IUserTaskNotificationStorage
{
    Task<UserTaskNotificationWriteResult> TryAdd(UserTaskNotification notification);
    Task<UserTaskNotification?> Get(Guid notificationId);
    Task<IReadOnlyList<UserTaskNotificationView>> GetForTasks(
        IEnumerable<Guid> userTaskIds,
        string ownerKey,
        DateTimeOffset? beforeUtc,
        int limit,
        bool unreadOnly);
    Task<bool> MarkRead(Guid notificationId, string ownerKey, DateTimeOffset readAtUtc);
}

public static class UserTaskNotificationContract
{
    public static void Validate(UserTaskNotification item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Id == Guid.Empty || item.UserTaskId == Guid.Empty
            || item.Kind is not ("follow_up" or "reminder" or "due" or "escalation")
            || string.IsNullOrWhiteSpace(item.DeduplicationKey) || item.DeduplicationKey.Length > 256)
            throw new ArgumentException("The user-task notification is invalid.", nameof(item));
    }

    public static void ValidateOwner(string ownerKey)
    {
        if (ownerKey.Length != 64 || ownerKey.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The notification owner key is invalid.", nameof(ownerKey));
    }
}

/// <summary>Kompatibilitätsadapter für externe Ablagen ohne Task-Meldungen.</summary>
internal sealed class UnsupportedUserTaskNotificationStorage : IUserTaskNotificationStorage
{
    internal static UnsupportedUserTaskNotificationStorage Instance { get; } = new();
    private UnsupportedUserTaskNotificationStorage() { }
    public Task<UserTaskNotificationWriteResult> TryAdd(UserTaskNotification notification) =>
        Unsupported<UserTaskNotificationWriteResult>();
    public Task<UserTaskNotification?> Get(Guid notificationId) => Unsupported<UserTaskNotification?>();
    public Task<IReadOnlyList<UserTaskNotificationView>> GetForTasks(IEnumerable<Guid> userTaskIds,
        string ownerKey, DateTimeOffset? beforeUtc, int limit, bool unreadOnly) =>
        Unsupported<IReadOnlyList<UserTaskNotificationView>>();
    public Task<bool> MarkRead(Guid notificationId, string ownerKey, DateTimeOffset readAtUtc) => Unsupported<bool>();
    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support user-task notifications."));
}
