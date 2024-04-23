using BPMN.Common;

namespace BPMN.Activities;

public record Transaction : SubProcess
{
    public required string Method { get; init; }
    public required string Protocol { get; init; }
}