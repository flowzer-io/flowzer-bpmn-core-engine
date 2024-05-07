using BPMN.Data;
using BPMN.Flowzer;

namespace BPMN.Events;

public abstract record ThrowEvent : Event, IFlowzerInputMapping
{
    public InputSet? InputSet { get; init; }
    public List<DataInput> DataInputs { get; init; } = [];
    public List<DataInputAssociation> DataInputAssociations { get; init; } = [];
    public List<EventDefinition> EventDefinitions { get; init; } = [];
    public List<FlowzerIoMapping>? InputMappings { get; init; }
}