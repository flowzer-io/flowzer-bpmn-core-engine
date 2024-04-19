using System.ComponentModel.DataAnnotations;

namespace BPMN.Common;

public class FormalExpression : Expression
{
    [Required] public string Body { get; set; } = "";
    [Required] public string Language { get; set; } = "";
    
    public ItemDefinition? EvaluatesToTypeRef { get; set; }
}