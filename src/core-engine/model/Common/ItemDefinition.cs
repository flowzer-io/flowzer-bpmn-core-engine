using Foundation;

namespace Common;

public class ItemDefinition : RootElement
{
    public ItemKind ItemKind { get; set; }
    public object? StructureRef { get; set; }
    public bool IsCollection { get; set; }
}