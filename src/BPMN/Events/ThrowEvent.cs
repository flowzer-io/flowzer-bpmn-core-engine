using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public abstract class ThrowEvent(string name, IFlowElementContainer container, InputSet inputSet) : Event(name, container)
{
    public InputSet InputSet { get; set; } = inputSet;
    public List<DataInput> DataInputs { get; set; } = [];
    public List<DataInputAssociation> DataInputAssociations { get; set; } = [];
    public List<EventDefinition> EventDefinitions { get; set; } = [];
}