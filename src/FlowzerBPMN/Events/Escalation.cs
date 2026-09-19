namespace BPMN.Events;

/// <summary>
/// Ein <c>bpmn:escalation</c>-Wurzelelement. Der <see cref="EscalationCode"/> ist der fachliche
/// Schlüssel, über den ein werfendes und ein fangendes Eskalationsereignis zueinander finden;
/// <see cref="FlowzerId"/> ist nur die XML-Kennung, mit der <c>escalationRef</c> darauf zeigt.
/// </summary>
public record Escalation : IRootElement
{
    public string EscalationCode { get; init; } = "";
    public string Name { get; init; } = "";

    public ItemDefinition? StructureRef { get; init; }

    public string? FlowzerId { get; init; }
}
