using System.ComponentModel.DataAnnotations;

namespace Data;

public class Property : ItemAwareElement
{
    [Required] public string Name { get; set; } = "";
}