namespace Data;

public class OutputSet(string name)
{
    public string Name { get; set; } = name;

    public List<DataOutput> DataOutputRefs { get; set; } = [];
    public List<DataOutput> OptionalOutputRefs { get; set; } = [];
    public List<DataOutput> WhileExecutingOutputRefs { get; set; } = [];
    public List<InputSet> InputSetRefs { get; set; } = [];
}