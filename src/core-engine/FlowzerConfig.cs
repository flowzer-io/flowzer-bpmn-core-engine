namespace core_engine;

public class FlowzerConfig
{
    public IExpressionHandler? ExpressionHandler { get; set; }

    public static FlowzerConfig Default => new FlowzerConfig()
    {
        ExpressionHandler = new FeelinExpressionHandler()
    };
}