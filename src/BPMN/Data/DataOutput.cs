namespace BPMN.Data;

public class DataOutput : ItemAwareElement
{
    public required string Name { get; set; }
    public bool IsCollection { get; set; }
    
    public List<OutputSet> OutputSetRefs { get; set; } = [];
    public List<OutputSet> OutputSetWithWhileExecuting { get; set; } = [];
    public List<OutputSet> OutputSetWithOptional { get; set; } = [];
}