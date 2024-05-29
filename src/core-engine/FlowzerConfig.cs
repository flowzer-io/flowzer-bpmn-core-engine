namespace core_engine;

public class FlowzerConfig
{
    public required IExpressionHandler ExpressionHandler { get; set; }

    public static FlowzerConfig Default => new FlowzerConfig()
    {
        ExpressionHandler = new FeelinExpressionHandler()
    };
}