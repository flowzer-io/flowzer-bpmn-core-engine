using FluentAssertions;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

public sealed class UserTaskScheduleResolverTest
{
    private static readonly DateTimeOffset ActivatedAt =
        new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    // Testzweck: Eine ISO-Dauer wird genau einmal relativ zum Aktivierungszeitpunkt
    // gebunden und nicht relativ zum Zeitpunkt eines späteren Scheduler-Laufs.
    [Test]
    public void Resolve_ShouldBindIsoDurationToActivation()
    {
        var result = UserTaskScheduleResolver.Resolve(
            Guid.NewGuid(), ActivatedAt, "PT48H", "PT24H", UserTaskDeadlinePolicy.Default);

        result.ScheduleState.Should().Be("resolved");
        result.DueAtUtc.Should().Be(ActivatedAt.AddHours(48));
        result.FollowUpAtUtc.Should().Be(ActivatedAt.AddHours(24));
        result.ReminderAtUtc.Should().Equal(
            ActivatedAt.AddHours(24),
            ActivatedAt.AddHours(47));
        result.EscalationAtUtc.Should().Be(ActivatedAt.AddHours(72));
        result.NextCheckAtUtc.Should().Be(ActivatedAt.AddHours(24));
    }

    // Testzweck: Absolute Zeitpunkte mit Offset werden in einen eindeutigen UTC-Wert
    // normalisiert, damit Browser und Server dieselbe Fälligkeit anzeigen.
    [Test]
    public void Resolve_ShouldNormalizeOffsetDateTime()
    {
        var result = UserTaskScheduleResolver.Resolve(
            Guid.NewGuid(), ActivatedAt, "2026-09-10T14:30:00+02:00", null,
            UserTaskDeadlinePolicy.Default);

        result.ScheduleState.Should().Be("resolved");
        result.DueAtUtc.Should().Be(new DateTimeOffset(2026, 9, 10, 12, 30, 0, TimeSpan.Zero));
    }

    // Testzweck: FEEL-Ausdrücke und lokale Zeitangaben ohne Offset werden nicht als
    // scheinbar exakte Termine interpretiert.
    [TestCase("=now() + duration(\"PT2H\")", "unsupported")]
    [TestCase("2026-09-10T14:30:00", "unsupported")]
    [TestCase("kein-termin", "invalid")]
    public void Resolve_ShouldExposeUnsupportedOrInvalidValues(string rawValue, string state)
    {
        var result = UserTaskScheduleResolver.Resolve(
            Guid.NewGuid(), ActivatedAt, rawValue, null, UserTaskDeadlinePolicy.Default);

        result.ScheduleState.Should().Be(state);
        result.DueAtUtc.Should().BeNull();
        result.NextCheckAtUtc.Should().BeNull();
    }

    // Testzweck: Eine Wiedervorlage nach der Fälligkeit ist ein widersprüchlicher
    // Vertrag und wird deshalb nicht teilweise automatisiert.
    [Test]
    public void Resolve_ShouldRejectFollowUpAfterDueDate()
    {
        var result = UserTaskScheduleResolver.Resolve(
            Guid.NewGuid(), ActivatedAt, "PT24H", "PT48H", UserTaskDeadlinePolicy.Default);

        result.ScheduleState.Should().Be("invalid");
        result.DueAtUtc.Should().BeNull();
        result.FollowUpAtUtc.Should().BeNull();
    }
}
