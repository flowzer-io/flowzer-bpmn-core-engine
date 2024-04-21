using BPMN.Foundation;

namespace BPMN.Data;

public class DataStore : ItemAwareElement, IRootElement
{
    public required string Name { get; set; }
    public bool IsUnlimited { get; set; }
    public int? Capacity { get; set; }
}