using BPMN.Common;
using BPMN.Data;
using BPMN.Flowzer;

namespace BPMN.Events;

public abstract record CatchEvent : Event, IFlowzerOutputMapping
{
    public OutputSet? OutputSet { get; init; }
    public List<DataOutput> DataOutputs { get; init; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; init; } = [];
    public EventDefinition? EventDefinition { get; init; }
    public List<FlowzerIoMapping>? OutputMappings { get; init; }
}