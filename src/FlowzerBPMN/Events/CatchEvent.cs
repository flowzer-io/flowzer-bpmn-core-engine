using BPMN.Data;
using BPMN.Flowzer;

namespace BPMN.Events;

public abstract record CatchEvent : Event, IFlowzerOutputMapping
{
    public OutputSet? OutputSet { get; init; }
    public FlowzerList<DataOutput>? DataOutputs { get; init; }
    public FlowzerList<DataOutputAssociation>? DataOutputAssociations { get; init; }
    public EventDefinition? EventDefinition { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}