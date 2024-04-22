using BPMN.Common;
using BPMN.Foundation;

namespace BPMN.Data;

public record ItemAwareElement : BaseElement
{
    public ItemDefinition? ItemSubjectRef { get; init; }
    public DataState? DataState { get; init; }
}