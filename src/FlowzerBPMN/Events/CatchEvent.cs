using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract record CatchEvent : Event
{
    public OutputSet? OutputSet { get; init; }
    public List<DataOutput> DataOutputs { get; init; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; init; } = [];
    public EventDefinition? EventDefinition { get; init; }
}