namespace BPMN.Activities;

public record Activity : FlowNode, IHasDefault
{
    public bool IsForCompensation { get; init; }
    public int StartQuantity { get; init; }
    public int CompletionQuantity { get; init; }
    
    public InputOutputSpecification? IoSpecification { get; init; }
    public FlowzerList<DataInputAssociation>? DataInputAssociations { get; init; }
    public FlowzerList<DataOutputAssociation>? DataOutputAssociations { get; init; }
    public FlowzerList<Property>? Properties { get; init; }

    public string? DefaultId { get; init; }
    public FlowzerList<ResourceRole>? Resources { get; init; }
    
    
    public LoopCharacteristics? LoopCharacteristics { get; init; }
    public FlowzerList<BoundaryEvent>? BoundaryEvents { get; init; }
}