using BPMN.Foundation;

namespace BPMN.Common;

public record Resource : IRootElement
{
    public required string Name { get; init; }
    
    public List<ResourceParameter> ResourceParameters { get; init; } = [];
}