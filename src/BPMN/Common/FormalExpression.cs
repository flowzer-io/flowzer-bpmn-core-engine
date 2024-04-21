namespace BPMN.Common;

public class FormalExpression : Expression
{
    public required string Body { get; set; }
    public required string Language { get; set; }
    
    public ItemDefinition? EvaluatesToTypeRef { get; set; }
}