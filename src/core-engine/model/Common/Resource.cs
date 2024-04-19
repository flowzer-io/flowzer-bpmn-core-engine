using System.ComponentModel.DataAnnotations;
using Foundation;

namespace Common;

public class Resource : RootElement
{
    [Required] public string Name { get; set; } = "";
    
    public List<ResourceParameter> ResourceParameters { get; set; } = [];
}