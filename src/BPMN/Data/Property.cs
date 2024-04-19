using System.ComponentModel.DataAnnotations;

namespace BPMN.Data;

public class Property : ItemAwareElement
{
    [Required] public string Name { get; set; } = "";
}