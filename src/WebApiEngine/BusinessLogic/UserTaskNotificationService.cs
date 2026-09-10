using Microsoft.AspNetCore.Authorization;
using Model;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>Objektberechtigter Zugriff auf den persistenten Task-Meldungsfeed.</summary>
public sealed class UserTaskNotificationService(
    BpmnBusinessLogic businessLogic,
    ICurrentUserContextAccessor currentUserAccessor,
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorization,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<NotificationDto>> GetAsync(
        DateTimeOffset? beforeUtc, int limit, bool unreadOnly)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var request = await Context();
        try
        {
            return await businessLogic.ExecuteUserTaskMutationAsync(async storage =>
            {
                var visible = new Dictionary<Guid, ExtendedUserTaskSubscription>();
                var tasks = (await storage.SubscriptionStorage.GetAllUserTasksExtended(request.User.UserId))
                    .OrderBy(task => task.Id).ToArray();
                foreach (var task in tasks)
                {
                    // Die Task-Sperre hält Rechteprüfung und Feed-Projektion in derselben
                    // Transaktionssicht wie Claim, Delegation und Abschluss.
                    if (!await storage.UserTaskLifecycleStorage.LockTask(task.Id)) continue;
                    var access = await UserTaskWorkAuthorization.EvaluateAsync(
                        storage, task, request.User, request.CanOperate);
                    if (access.CanSee) visible[task.Id] = task;
                }

                var notifications = await storage.UserTaskNotificationStorage.GetForTasks(
                    visible.Keys, request.OwnerKey, beforeUtc?.ToUniversalTime(), limit, unreadOnly);
                return (IReadOnlyList<NotificationDto>)notifications
                    .Where(item => visible.ContainsKey(item.Notification.UserTaskId))
                    .Select(item => ToDto(item, visible[item.Notification.UserTaskId]))
                    .ToArray();
            }, httpContextAccessor.HttpContext?.RequestAborted ?? default);
        }
        catch (NotSupportedException exception)
        {
            throw new UserTaskNotificationUnavailableException(exception);
        }
    }

    public async Task<bool> MarkReadAsync(Guid notificationId)
    {
        var request = await Context();
        try
        {
            return await businessLogic.ExecuteUserTaskMutationAsync(async storage =>
            {
                var notification = await storage.UserTaskNotificationStorage.Get(notificationId);
                if (notification is null || !await storage.UserTaskLifecycleStorage.LockTask(notification.UserTaskId))
                    return false;
                // Nach dem Lock erneut lesen: Ein Abschluss in einem anderen Prozess kann die
                // Meldung samt Task unmittelbar vor der Sperre entfernt haben.
                notification = await storage.UserTaskNotificationStorage.Get(notificationId);
                if (notification is null) return false;
                var task = await storage.SubscriptionStorage.GetUserTaskExtended(notification.UserTaskId);
                if (task is null) return false;
                var access = await UserTaskWorkAuthorization.EvaluateAsync(storage, task, request.User, request.CanOperate);
                if (!access.CanSee) return false;
                return await storage.UserTaskNotificationStorage.MarkRead(
                    notificationId, request.OwnerKey, timeProvider.GetUtcNow());
            }, httpContextAccessor.HttpContext?.RequestAborted ?? default);
        }
        catch (NotSupportedException exception)
        {
            throw new UserTaskNotificationUnavailableException(exception);
        }
    }

    private async Task<(CurrentUserContext User, bool CanOperate, string OwnerKey)> Context()
    {
        var user = currentUserAccessor.GetCurrentUser();
        user.RequireResolvedUserId("reading user-task notifications");
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("A request context is required for notifications.");
        var canOperate = (await authorization.AuthorizeAsync(principal, FlowzerPolicies.Operator)).Succeeded;
        return (user, canOperate, UserTaskDraftOwnerKey.Create(user));
    }

    private static NotificationDto ToDto(
        UserTaskNotificationView view,
        ExtendedUserTaskSubscription task)
    {
        var item = view.Notification;
        var (title, message, severity) = item.Kind switch
        {
            "follow_up" => ("Aufgabe wieder vorlegen", $"„{task.Name}“ ist zur Wiedervorlage fällig.", "info"),
            "reminder" => ("Fälligkeit nähert sich", $"„{task.Name}“ wird bald fällig.", "warning"),
            "due" => ("Aufgabe ist fällig", $"„{task.Name}“ hat die Fälligkeit erreicht.", "warning"),
            "escalation" => ("Aufgabe ist überfällig", $"„{task.Name}“ benötigt Klärung.", "error"),
            _ => ("Aufgabenmeldung", $"„{task.Name}“ wurde aktualisiert.", "info")
        };
        return new NotificationDto
        {
            Id = item.Id,
            UserTaskId = item.UserTaskId,
            Kind = item.Kind,
            OccurredAtUtc = item.OccurredAtUtc,
            ReadAtUtc = view.ReadAtUtc,
            Title = title,
            Message = message,
            Severity = severity,
            Href = $"/tasks?task={Uri.EscapeDataString(item.UserTaskId.ToString())}"
        };
    }
}

public sealed class UserTaskNotificationUnavailableException(Exception innerException)
    : Exception("User-task notifications are unavailable.", innerException);
