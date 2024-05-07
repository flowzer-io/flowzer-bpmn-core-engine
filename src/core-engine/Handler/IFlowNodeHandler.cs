namespace core_engine.Handler;

public interface IFlowNodeHandler
{
    public Variables? Execute(ProcessInstance processInstance, Token token);
}