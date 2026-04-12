using core_engine.Exceptions;

namespace core_engine;

/// <summary>
/// Kapselt die Berechnung von Fälligkeitszeitpunkten für Timerdefinitionen,
/// damit Start- und Laufzeitpfade dieselbe Logik verwenden.
/// </summary>
internal static class TimerDueDateCalculator
{
    public static DateTime GetDueDate(DateTime referenceTime, TimerEventDefinition timerDefinition, FlowNode flowNode)
    {
        ArgumentNullException.ThrowIfNull(timerDefinition);
        ArgumentNullException.ThrowIfNull(flowNode);

        if (timerDefinition.TimeCycle != null)
        {
            var timeSpan = ISO8601Date.DateExtensions.ParseIso8601Duration(timerDefinition.TimeCycle.Body);
            return referenceTime.Add(timeSpan);
        }

        if (timerDefinition.TimeDuration != null)
        {
            var timeSpan = ISO8601Date.DateExtensions.ParseIso8601Duration(timerDefinition.TimeDuration.Body);
            return referenceTime.Add(timeSpan);
        }

        if (timerDefinition.TimeDate != null)
        {
            return DateTime.Parse(timerDefinition.TimeDate.Body);
        }

        var nodeIdentifier = string.IsNullOrWhiteSpace(flowNode.Name)
            ? flowNode.Id
            : $"{flowNode.Id} ({flowNode.Name})";

        throw new ModelValidationException($"Timer definition is invalid for node {nodeIdentifier}.");
    }
}
