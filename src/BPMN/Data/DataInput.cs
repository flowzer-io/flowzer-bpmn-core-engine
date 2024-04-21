namespace BPMN.Data;

public class DataInput : ItemAwareElement
{
    public required string Name { get; set; }
    public bool IsCollection { get; set; }
    
    public List<InputSet> InputSetRefs { get; set; } = [];
    public List<InputSet> InputSetWithWhileExecuting { get; set; } = [];
    public List<InputSet> InputSetWithOptional { get; set; } = [];
}