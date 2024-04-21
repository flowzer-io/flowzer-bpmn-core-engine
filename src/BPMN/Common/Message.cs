using BPMN.Foundation;

namespace BPMN.Common;

public class Message : RootElement
{
    public required string Name { get; set; }
    public ItemDefinition? ItemRef { get; set; }
}