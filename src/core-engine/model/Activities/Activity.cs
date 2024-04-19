using Common;
using Data;
using Events;
using Process;

namespace Activities;

public class Activity : FlowNode
{
    public bool IsForCompensation { get; set; }
    public int StartQuantity { get; set; }
    public int CompletionQuantity { get; set; }
    
    public InputOutputSpecification? IoSpecification { get; set; }
    public List<DataInputAssociation> DataInputAssociations { get; set; } = [];
    public List<DataOutputAssociation> DataOutputAssociations { get; set; } = [];
    public List<Property> Properties { get; set; } = [];
    public FlowNode? Default { get; set; }
    public List<ResourceRole> Resources { get; set; } = [];
    public LoopCharacteristics? LoopCharacteristics { get; set; }
    public List<BoundaryEvent> BoundaryEvents { get; set; } = [];
}