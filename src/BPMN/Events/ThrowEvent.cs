using BPMN.Data;

namespace BPMN.Events;

public abstract class ThrowEvent : Event
{
    public required InputSet InputSet { get; set; }
    public List<DataInput> DataInputs { get; set; } = [];
    public List<DataInputAssociation> DataInputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}