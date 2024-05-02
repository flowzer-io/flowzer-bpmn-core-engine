namespace BPMN.HumanInteraction;

public record UserTask : Activities.Task
{
    public required string Implementation { get; init; }

    public List<Rendering> Renderings { get; init; } = [];

    public string? FlowzerAssignee { get; init; }
    public string? FlowzerCandidateGroups { get; init; }
    public string? FlowzerCandidateUsers { get; init; }
    public string? FlowzerDueDate { get; init; }
    public string? FlowzerFollowUpDate { get; init; }
    public string? FlowzerPriority { get; init; }
}