using Data;

namespace Events;

public abstract class ThrowEvent(InputSet inputSet) : Event
{
    public InputSet InputSet { get; set; } = inputSet;
    public List<DataInput> DataInputs { get; set; } = [];
    public List<DataInputAssociation> DataInputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}