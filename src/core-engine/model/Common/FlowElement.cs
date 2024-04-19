using System.ComponentModel.DataAnnotations;
using Artifacts;
using Foundation;
using Process;

namespace Common;

public abstract class FlowElement : BaseElement
{
    [Required] public string Name { get; set; } = "";
    [Required] public IFlowElementContainer Container { get; set; }
    public Auditing? Auditing { get; set; }
    public Monitoring? Monitoring { get; set; }
    public List<CategoryValue> CategoryValueRefs { get; set; } = [];
}