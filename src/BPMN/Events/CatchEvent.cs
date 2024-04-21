using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract class CatchEvent : Event
{
    public required OutputSet OutputSet { get; set; }
    public List<DataOutput> DataOutputs { get; set; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}