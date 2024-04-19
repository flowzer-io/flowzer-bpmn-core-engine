using System.ComponentModel.DataAnnotations;
using BPMN.Foundation;

namespace BPMN.Common;

public class Resource : RootElement
{
    [Required] public string Name { get; set; } = "";
    
    public List<ResourceParameter> ResourceParameters { get; set; } = [];
}