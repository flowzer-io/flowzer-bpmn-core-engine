namespace core_engine.Handler;

public interface IFlowNodeHandler
{
    public void Execute(ProcessInstance processInstance, Token token);
    public  List<Token>? GenerateOutgoingTokens(FlowzerConfig flowzerConfig, ProcessInstance processInstance, Token token);
}