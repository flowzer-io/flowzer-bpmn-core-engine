namespace BPMN.Common;

public record Resource : IRootElement
{
    public required string Name { get; init; }
    
    public FlowzerList<ResourceParameter> ResourceParameters { get; init; } = [];
}