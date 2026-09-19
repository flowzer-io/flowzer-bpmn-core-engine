namespace BPMN.Common;

/// <summary>
/// Ein <c>bpmn:error</c>-Wurzelelement. Der <see cref="ErrorCode"/> ist der fachliche
/// Schlüssel, über den ein Error-End-Event und ein Error-Boundary-Event zueinander finden;
/// <see cref="FlowzerId"/> ist nur die XML-Kennung, mit der <c>errorRef</c> darauf zeigt.
/// </summary>
public record Error : IRootElement
{
    public required string Name { get; init; }
    public string? ErrorCode { get; init; }

    public ItemDefinition? StructureRef { get; init; }

    public string? FlowzerId { get; init; }
}
