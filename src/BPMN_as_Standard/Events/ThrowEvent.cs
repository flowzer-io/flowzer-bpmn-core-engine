using BPMN.Data;

namespace BPMN.Events;

public abstract record ThrowEvent : Event
{
    public required InputSet InputSet { get; init; }
    public List<DataInput> DataInputs { get; init; } = [];
    public List<DataInputAssociation> DataInputAssociations { get; init; } = [];
    public List<EventDefinition> EventDefinitions { get; init; } = [];
}