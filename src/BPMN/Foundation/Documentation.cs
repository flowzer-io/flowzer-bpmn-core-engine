using System.ComponentModel.DataAnnotations;

namespace BPMN.Foundation;

public class Documentation : BaseElement
{
    [Required] public string Text { get; set; } = "";
    public string? TextFormat { get; set; }
}