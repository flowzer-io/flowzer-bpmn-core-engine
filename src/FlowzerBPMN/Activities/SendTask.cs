namespace BPMN.Activities;

public record SendTask : Task, IFlowzerInputMapping
{ 
    public required string Implementation { get; init; }
    
    public MessageDefinition? MessageRef { get; init; }
    public Operation? OperationRef { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
}