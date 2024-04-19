using System.ComponentModel.DataAnnotations;

namespace BPMN.Common;

public class Error
{
    [Required] public string Name { get; set; } = "";
    public string? ErrorCode { get; set; }
    
    public ItemDefinition? StructureRef { get; set; }
}