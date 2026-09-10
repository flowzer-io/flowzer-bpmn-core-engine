using FluentAssertions;
using Model;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

public sealed class UserTaskDeadlineProcessorTest
{
    private static readonly DateTimeOffset DueAt =
        new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    // Testzweck: Ein Nachhollauf erzeugt alle bis jetzt fälligen Meilensteine genau
    // einmal und plant als Nächstes ausschließlich die spätere Eskalation.
    [Test]
    public void Advance_ShouldCatchUpDueMilestonesAndScheduleEscalation()
    {
        var deadline = Deadline();

        var result = UserTaskDeadlineProcessor.Advance(deadline, DueAt);

        result.Notifications.Select(item => item.Kind).Should()
            .Equal("follow_up", "reminder", "due");
        result.Deadline.Revision.Should().Be(2);
        result.Deadline.Status.Should().Be("overdue");
        result.Deadline.NextCheckAtUtc.Should().Be(DueAt.AddDays(1));
    }

    // Testzweck: Ein erneut ausgeführter Schedulerlauf erzeugt keine bereits
    // protokollierten Meldungen und ändert den Status nicht unnötig.
    [Test]
    public void Advance_ShouldNotRepeatEmittedMilestones()
    {
        var first = UserTaskDeadlineProcessor.Advance(Deadline(), DueAt);

        var replay = UserTaskDeadlineProcessor.Advance(first.Deadline, DueAt.AddHours(1));

        replay.Notifications.Should().BeEmpty();
        replay.Deadline.Should().BeSameAs(first.Deadline);
    }

    // Testzweck: Die zeitbasierte Eskalation wird nach Ablauf einmal gemeldet, ohne
    // eine BPMN-Eskalation oder automatische Zuweisung vorzutäuschen.
    [Test]
    public void Advance_ShouldEmitEscalationAtConfiguredInstant()
    {
        var caughtUp = UserTaskDeadlineProcessor.Advance(Deadline(), DueAt).Deadline;

        var result = UserTaskDeadlineProcessor.Advance(caughtUp, DueAt.AddDays(1));

        result.Notifications.Should().ContainSingle().Which.Kind.Should().Be("escalation");
        result.Deadline.Status.Should().Be("escalated");
        result.Deadline.NextCheckAtUtc.Should().BeNull();
    }

    private static UserTaskDeadline Deadline()
    {
        var taskId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        return new UserTaskDeadline
        {
            UserTaskId = taskId,
            Revision = 1,
            ActivatedAtUtc = DueAt.AddDays(-2),
            RawDueDate = "P2D",
            RawFollowUpDate = "P1D",
            ScheduleState = "resolved",
            Status = "scheduled",
            DueAtUtc = DueAt,
            FollowUpAtUtc = DueAt.AddDays(-1),
            ReminderAtUtc = [DueAt.AddHours(-1)],
            EscalationAtUtc = DueAt.AddDays(1),
            NextCheckAtUtc = DueAt.AddDays(-1),
            PolicyVersion = "test-v1",
            UpdatedAtUtc = DueAt.AddDays(-2)
        };
    }
}

