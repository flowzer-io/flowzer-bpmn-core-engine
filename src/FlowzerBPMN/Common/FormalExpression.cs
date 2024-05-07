namespace BPMN.Common;

public record FormalExpression : Expression
{
    public required string Language { get; init; }
    
    public ItemDefinition? EvaluatesToTypeRef { get; init; }
}