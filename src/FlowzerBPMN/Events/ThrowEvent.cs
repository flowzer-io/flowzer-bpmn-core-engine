using BPMN.Data;
using BPMN.Flowzer;

namespace BPMN.Events;

public abstract record ThrowEvent : Event, IFlowzerInputMapping
{
    public InputSet? InputSet { get; init; }
    public ImmutableList<DataInput>? DataInputs { get; init; }
    public ImmutableList<DataInputAssociation>? DataInputAssociations { get; init; }
    public ImmutableList<EventDefinition>? EventDefinitions { get; init; }
    public ImmutableList<FlowzerIoMapping>? InputMappings { get; init; }
}