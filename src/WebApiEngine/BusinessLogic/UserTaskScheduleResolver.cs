using System.Globalization;
using System.Xml;
using Model;

namespace WebApiEngine.BusinessLogic;

/// <summary>Unveränderliche, bereits geprüfte Deadline-Policy einer Installation.</summary>
public sealed record UserTaskDeadlinePolicy(
    string Version,
    IReadOnlyList<TimeSpan> ReminderLeadTimes,
    TimeSpan EscalationAfterDue)
{
    public static UserTaskDeadlinePolicy Default { get; } = new(
        "default-v1",
        [TimeSpan.FromDays(1), TimeSpan.FromHours(1)],
        TimeSpan.FromDays(1));
}

/// <summary>
/// Bindet die bewusst kleine, deterministische Human-Task-Zeitteilmenge. Beliebige
/// Ausdrücke werden nicht mit einer möglicherweise abweichenden Client-Semantik geraten.
/// </summary>
public static class UserTaskScheduleResolver
{
    public static UserTaskDeadline Resolve(
        Guid taskId,
        DateTimeOffset activatedAtUtc,
        string? rawDueDate,
        string? rawFollowUpDate,
        UserTaskDeadlinePolicy policy)
    {
        var due = Parse(rawDueDate, activatedAtUtc);
        var followUp = Parse(rawFollowUpDate, activatedAtUtc);
        var state = CombineState(due.State, followUp.State);
        if (state == "resolved" && due.Value is { } dueAt && followUp.Value is { } followUpAt
            && followUpAt > dueAt)
            state = "invalid";

        var usable = state is "resolved" or "none";
        var boundDue = usable ? due.Value : null;
        var boundFollowUp = usable ? followUp.Value : null;
        var reminders = boundDue is { } deadline
            ? policy.ReminderLeadTimes
                .Where(lead => lead > TimeSpan.Zero)
                .Select(lead => deadline - lead)
                .Where(at => at >= activatedAtUtc)
                .Distinct()
                .Order()
                .ToArray()
            : [];
        var escalation = boundDue is { } dueAtUtc && policy.EscalationAfterDue >= TimeSpan.Zero
            ? dueAtUtc + policy.EscalationAfterDue
            : (DateTimeOffset?)null;
        var next = reminders.Cast<DateTimeOffset?>()
            .Append(boundFollowUp)
            .Append(boundDue)
            .Append(escalation)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Min();

        return new UserTaskDeadline
        {
            UserTaskId = taskId,
            Revision = 1,
            ActivatedAtUtc = activatedAtUtc.ToUniversalTime(),
            RawDueDate = Normalize(rawDueDate),
            RawFollowUpDate = Normalize(rawFollowUpDate),
            ScheduleState = state,
            Status = state == "resolved" ? "scheduled" : state,
            DueAtUtc = boundDue,
            FollowUpAtUtc = boundFollowUp,
            ReminderAtUtc = reminders,
            EscalationAtUtc = escalation,
            NextCheckAtUtc = next == default ? null : next,
            PolicyVersion = policy.Version,
            UpdatedAtUtc = activatedAtUtc.ToUniversalTime()
        };
    }

    private static (string State, DateTimeOffset? Value) Parse(string? raw, DateTimeOffset activatedAt)
    {
        var value = Normalize(raw);
        if (value is null) return ("none", null);
        if (value.StartsWith('=')) return ("unsupported", null);

        try
        {
            if (value.StartsWith('P'))
                return ("resolved", activatedAt.ToUniversalTime() + XmlConvert.ToTimeSpan(value));
        }
        catch (FormatException)
        {
            return ("invalid", null);
        }

        if (LooksLikeLocalDateTime(value)) return ("unsupported", null);

        if (HasExplicitOffset(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var absolute))
            return ("resolved", absolute.ToUniversalTime());
        return ("invalid", null);
    }

    private static string CombineState(string due, string followUp)
    {
        if (due == "invalid" || followUp == "invalid") return "invalid";
        if (due == "unsupported" || followUp == "unsupported") return "unsupported";
        if (due == "none" && followUp == "none") return "none";
        return "resolved";
    }

    private static bool HasExplicitOffset(string value) =>
        value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
        || (value.Length >= 6 && (value[^6] is '+' or '-') && value[^3] == ':');

    private static bool LooksLikeLocalDateTime(string value) =>
        value.Contains('T') && !HasExplicitOffset(value);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
