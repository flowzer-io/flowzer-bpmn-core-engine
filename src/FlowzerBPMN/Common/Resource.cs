using BPMN.Foundation;

namespace BPMN.Common;

public record Resource : IRootElement
{
    public required string Name { get; init; }
    
    public ImmutableList<ResourceParameter> ResourceParameters { get; init; } = [];
}