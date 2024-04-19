using System.ComponentModel.DataAnnotations;
using Common;
using Foundation;

namespace Activities;

public class ResourceRole : BaseElement
{
    [Required] public string Name { get; set; } = "";
    
    public Process.Process? Process { get; set; }
    public Resource? ResourceRef { get; set; }
    public List<ResourceParameterBinding> ResourceParameterBindings { get; set; } = [];
    public ResourceAssignmentExpression? ResourceAssignmentExpression { get; set; }
}