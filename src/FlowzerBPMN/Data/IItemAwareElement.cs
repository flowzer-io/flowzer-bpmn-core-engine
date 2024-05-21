namespace BPMN.Data;

public interface IItemAwareElement : IBaseElement
{
    public ItemDefinition? ItemSubjectRef { get; init; }
    public DataState? DataState { get; init; }
}