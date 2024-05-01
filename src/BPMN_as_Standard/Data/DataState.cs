using BPMN.Foundation;

namespace BPMN.Data;

public record DataState : BaseElement
{
    public string? Name { get; init; }
}