using BPMN.Flowzer;

namespace BPMN.HumanInteraction;

public record UserTask : Activities.Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string Implementation { get; init; }

    public ImmutableList<Rendering> Renderings { get; init; } = [];

    public string? FlowzerAssignee { get; init; }
    public string? FlowzerCandidateGroups { get; init; }
    public string? FlowzerCandidateUsers { get; init; }
    public string? FlowzerDueDate { get; init; }
    public string? FlowzerFollowUpDate { get; init; }
    public string? FlowzerPriority { get; init; }
    public ImmutableList<FlowzerIoMapping>? InputMappings { get; init; }
    public ImmutableList<FlowzerIoMapping>? OutputMappings { get; init; }
}