using System.ComponentModel.DataAnnotations;
using Foundation;

namespace Common;

public class Message : RootElement
{
    [Required] public string Name { get; set; } = "";
    public ItemDefinition? ItemRef { get; set; }
}