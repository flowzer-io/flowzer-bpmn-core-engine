using Data;

namespace Events;

public abstract class CatchEvent(OutputSet outputSet) : Event
{
    public OutputSet OutputSet { get; set; } = outputSet;
    public List<DataOutput> DataOutputs { get; set; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}