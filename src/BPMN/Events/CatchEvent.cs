using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract class CatchEvent(string name, IFlowElementContainer container, OutputSet outputSet) : Event(name, container)
{
    public OutputSet OutputSet { get; set; } = outputSet;
    public List<DataOutput> DataOutputs { get; set; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}