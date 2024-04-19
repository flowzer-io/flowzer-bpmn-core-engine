using Foundation;

namespace Data;

public class DataStore(string name) : ItemAwareElement, IRootElement
{
    public string Name { get; set; } = name;
    public bool IsUnlimited { get; set; }
    public int? Capacity { get; set; }
}