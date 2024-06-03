using core_engine.Expression;
using core_engine.Expression.Feelin;


namespace core_engine;

public class FlowzerConfig
{
    public required IExpressionHandler ExpressionHandler { get; init; }

    public static FlowzerConfig Default => new()
    {
        ExpressionHandler = new FeelinExpressionHandler()
    };
}