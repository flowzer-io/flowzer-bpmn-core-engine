using BPMN.Foundation;

namespace BPMN.Data;

public class DataStore(string name) : ItemAwareElement, IRootElement
{
    public string Name { get; set; } = name;
    public bool IsUnlimited { get; set; }
    public int? Capacity { get; set; }
}