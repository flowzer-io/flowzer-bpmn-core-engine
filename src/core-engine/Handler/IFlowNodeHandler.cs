namespace core_engine.Handler;

public interface IFlowNodeHandler
{
    public void Execute(ProcessInstance processInstance, Token token);
}