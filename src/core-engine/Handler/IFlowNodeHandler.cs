namespace core_engine.Handler;

public interface IFlowNodeHandler
{
    public void Execute(InstanceEngine processInstance, Token token);
    public  List<Token>? GenerateOutgoingTokens(FlowzerConfig flowzerConfig, InstanceEngine processInstance, Token token);
}