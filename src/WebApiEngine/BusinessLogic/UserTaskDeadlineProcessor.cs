using Model;

namespace WebApiEngine.BusinessLogic;

public sealed record UserTaskDeadlineAdvance(
    UserTaskDeadline Deadline,
    IReadOnlyList<UserTaskNotification> Notifications);

/// <summary>Deterministische, nebenwirkungsfreie Ermittlung fälliger Task-Meilensteine.</summary>
public static class UserTaskDeadlineProcessor
{
    public static UserTaskDeadlineAdvance Advance(UserTaskDeadline deadline, DateTimeOffset nowUtc)
    {
        if (deadline.ScheduleState != "resolved" || deadline.NextCheckAtUtc is null
            || deadline.NextCheckAtUtc > nowUtc)
            return new(deadline, []);

        var emitted = deadline.EmittedMilestones.ToHashSet(StringComparer.Ordinal);
        var notifications = new List<UserTaskNotification>();
        Add("follow_up", deadline.FollowUpAtUtc);
        for (var index = 0; index < deadline.ReminderAtUtc.Length; index++)
            Add($"reminder:{index}", deadline.ReminderAtUtc[index], "reminder");
        Add("due", deadline.DueAtUtc);
        Add("escalation", deadline.EscalationAtUtc);

        if (notifications.Count == 0) return new(deadline, []);

        var remaining = Milestones(deadline)
            .Where(item => !emitted.Contains(item.Key))
            .Select(item => item.At)
            .Order()
            .ToArray();
        var status = emitted.Contains("escalation") ? "escalated"
            : emitted.Contains("due") ? "overdue"
            : emitted.Contains("follow_up") ? "follow_up_due"
            : "scheduled";
        var advanced = deadline with
        {
            Revision = checked(deadline.Revision + 1),
            EmittedMilestones = emitted.Order(StringComparer.Ordinal).ToArray(),
            NextCheckAtUtc = remaining.Length == 0 ? null : remaining[0],
            Status = status,
            UpdatedAtUtc = nowUtc.ToUniversalTime()
        };
        return new(advanced, notifications);

        void Add(string key, DateTimeOffset? at, string? kind = null)
        {
            if (at is null || at > nowUtc || !emitted.Add(key)) return;
            notifications.Add(new UserTaskNotification
            {
                Id = Guid.NewGuid(),
                UserTaskId = deadline.UserTaskId,
                Kind = kind ?? key,
                DeduplicationKey = $"task:{deadline.UserTaskId:N}:{deadline.PolicyVersion}:{key}:{at:O}",
                OccurredAtUtc = at.Value.ToUniversalTime()
            });
        }
    }

    private static IEnumerable<(string Key, DateTimeOffset At)> Milestones(UserTaskDeadline deadline)
    {
        if (deadline.FollowUpAtUtc is { } followUp) yield return ("follow_up", followUp);
        for (var index = 0; index < deadline.ReminderAtUtc.Length; index++)
            yield return ($"reminder:{index}", deadline.ReminderAtUtc[index]);
        if (deadline.DueAtUtc is { } due) yield return ("due", due);
        if (deadline.EscalationAtUtc is { } escalation) yield return ("escalation", escalation);
    }
}
