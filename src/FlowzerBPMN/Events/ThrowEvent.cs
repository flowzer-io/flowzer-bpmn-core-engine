namespace BPMN.Events;

public abstract record ThrowEvent : Event, IFlowzerInputMapping
{
    public InputSet? InputSet { get; init; }
    public FlowzerList<DataInput>? DataInputs { get; init; }
    public FlowzerList<DataInputAssociation>? DataInputAssociations { get; init; }
    public FlowzerList<EventDefinition>? EventDefinitions { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
}