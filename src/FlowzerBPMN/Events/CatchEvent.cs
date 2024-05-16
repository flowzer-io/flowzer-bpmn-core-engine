using BPMN.Data;
using BPMN.Flowzer;

namespace BPMN.Events;

public abstract record CatchEvent : Event, IFlowzerOutputMapping
{
    public OutputSet? OutputSet { get; init; }
    public ImmutableList<DataOutput>? DataOutputs { get; init; }
    public ImmutableList<DataOutputAssociation>? DataOutputAssociations { get; init; }
    public EventDefinition? EventDefinition { get; init; }
    public ImmutableList<FlowzerIoMapping>? OutputMappings { get; init; }
}