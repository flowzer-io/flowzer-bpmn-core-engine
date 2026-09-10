using System.Xml;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Background;

public sealed class UserTaskDeadlineOptions
{
    public const string SectionName = "UserTaskDeadlines";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 100;
    public string PolicyVersion { get; set; } = "default-v1";
    public string[] ReminderLeadTimes { get; set; } = ["P1D", "PT1H"];
    public string EscalationAfterDue { get; set; } = "P1D";

    public bool IsValid()
    {
        if (PollIntervalSeconds is < 1 or > 3600 || BatchSize is < 1 or > 1000
            || string.IsNullOrWhiteSpace(PolicyVersion) || PolicyVersion.Length > 64)
            return false;
        try
        {
            return ReminderLeadTimes.Length <= 10
                   && ReminderLeadTimes.Select(XmlConvert.ToTimeSpan).All(value => value > TimeSpan.Zero)
                   && XmlConvert.ToTimeSpan(EscalationAfterDue) >= TimeSpan.Zero;
        }
        catch (FormatException) { return false; }
    }

    public UserTaskDeadlinePolicy ToPolicy()
    {
        if (!IsValid()) throw new InvalidOperationException("UserTaskDeadlines configuration is invalid.");
        return new UserTaskDeadlinePolicy(
            PolicyVersion.Trim(),
            ReminderLeadTimes.Select(XmlConvert.ToTimeSpan).Distinct().OrderByDescending(value => value).ToArray(),
            XmlConvert.ToTimeSpan(EscalationAfterDue));
    }
}

