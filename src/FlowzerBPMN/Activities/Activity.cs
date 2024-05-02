using BPMN.Common;
using BPMN.Data;
using BPMN.Events;

namespace BPMN.Activities;

public record Activity : FlowNode, IHasDefault
{
    public bool IsForCompensation { get; init; }
    public int StartQuantity { get; init; }
    public int CompletionQuantity { get; init; }
    
    public InputOutputSpecification? IoSpecification { get; init; }
    public List<DataInputAssociation> DataInputAssociations { get; init; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; init; } = [];
    public List<Property> Properties { get; init; } = [];
    public SequenceFlow? Default { get; set; }
    public string? DefaultId { get; init; }
    public List<ResourceRole> Resources { get; init; } = [];
    public LoopCharacteristics? LoopCharacteristics { get; init; }
    public List<BoundaryEvent> BoundaryEvents { get; init; } = [];
}

public interface IHasDefault
{
    public SequenceFlow? Default { get; set; }
    public string? DefaultId { get; init; }
}