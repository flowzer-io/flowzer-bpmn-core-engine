namespace BPMN.Data;

public class OutputSet
{
    public required string Name { get; set; }

    public List<DataOutput> DataOutputRefs { get; set; } = [];
    public List<DataOutput> OptionalOutputRefs { get; set; } = [];
    public List<DataOutput> WhileExecutingOutputRefs { get; set; } = [];
    public List<InputSet> InputSetRefs { get; set; } = [];
}