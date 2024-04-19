using System.ComponentModel.DataAnnotations;

namespace Data;

public class DataInput : ItemAwareElement
{
    [Required] public string Name { get; set; } = "";
    public bool IsCollection { get; set; }
    
    public List<InputSet> InputSetRefs { get; set; } = [];
    public List<InputSet> InputSetWithWhileExecuting { get; set; } = [];
    public List<InputSet> InputSetWithOptional { get; set; } = [];
}