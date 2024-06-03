namespace core_engine.Expression;

public interface IExpressionHandler
{
    public abstract object? GetValue(object obj, string expression);
    
    public abstract bool MatchExpression(object obj, string expression);

    public string ResolveString(object obj, string expression);
}