namespace core_engine;

public class FlowzerConfig
{
    public required ExpressionHandler ExpressionHandler { get; init; }

    public static FlowzerConfig Default => new()
    {
        ExpressionHandler = new FeelinExpressionHandler()
    };
}