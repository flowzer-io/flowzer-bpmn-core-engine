using System.Globalization;
using core_engine.ISO8601Date;

namespace core_engine;

public readonly record struct TimerSchedule(
    DateTime DueAt,
    TimeSpan? RepeatInterval,
    int? RemainingOccurrences);

public static class TimerScheduleCalculator
{
    public static TimerSchedule CreateInitialSchedule(
        DateTime referenceTime,
        TimerEventDefinition timerDefinition,
        FlowNode flowNode)
    {
        ArgumentNullException.ThrowIfNull(timerDefinition);
        ArgumentNullException.ThrowIfNull(flowNode);

        if (timerDefinition.TimeCycle != null &&
            TryParseRepeatingCycle(timerDefinition.TimeCycle.Body, out var repeatingCycle))
        {
            return new TimerSchedule(
                referenceTime.Add(repeatingCycle.Interval),
                repeatingCycle.Interval,
                repeatingCycle.RemainingOccurrences);
        }

        return new TimerSchedule(
            TimerDueDateCalculator.GetDueDate(referenceTime, timerDefinition, flowNode),
            null,
            1);
    }

    public static bool TryAdvanceSchedule(TimerSchedule currentSchedule, out TimerSchedule nextSchedule)
    {
        if (currentSchedule.RepeatInterval == null)
        {
            nextSchedule = default;
            return false;
        }

        if (currentSchedule.RemainingOccurrences is int remainingOccurrences)
        {
            var nextRemainingOccurrences = remainingOccurrences - 1;
            if (nextRemainingOccurrences <= 0)
            {
                nextSchedule = default;
                return false;
            }

            nextSchedule = currentSchedule with
            {
                DueAt = currentSchedule.DueAt.Add(currentSchedule.RepeatInterval.Value),
                RemainingOccurrences = nextRemainingOccurrences
            };
            return true;
        }

        nextSchedule = currentSchedule with
        {
            DueAt = currentSchedule.DueAt.Add(currentSchedule.RepeatInterval.Value)
        };
        return true;
    }

    private static bool TryParseRepeatingCycle(
        string expression,
        out RepeatingCycleDefinition repeatingCycleDefinition)
    {
        repeatingCycleDefinition = default;

        if (string.IsNullOrWhiteSpace(expression) ||
            !expression.StartsWith("R", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = expression.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        int? remainingOccurrences = null;
        var repetitionToken = parts[0][1..];
        if (!string.IsNullOrWhiteSpace(repetitionToken))
        {
            if (!int.TryParse(repetitionToken, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCount))
            {
                return false;
            }

            remainingOccurrences = parsedCount;
        }

        repeatingCycleDefinition = new RepeatingCycleDefinition(
            DateExtensions.ParseIso8601Duration(parts[1]),
            remainingOccurrences);
        return true;
    }

    private readonly record struct RepeatingCycleDefinition(
        TimeSpan Interval,
        int? RemainingOccurrences);
}
