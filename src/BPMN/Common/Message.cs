using System.ComponentModel.DataAnnotations;
using BPMN.Foundation;

namespace BPMN.Common;

public class Message : RootElement
{
    [Required] public string Name { get; set; } = "";
    public ItemDefinition? ItemRef { get; set; }
}