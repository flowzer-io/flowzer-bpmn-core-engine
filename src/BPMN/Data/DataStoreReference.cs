namespace BPMN.Data;

public record DataStoreReference : ItemAwareElement
{
    public DataStore? DataStoreRef { get; init; }
}