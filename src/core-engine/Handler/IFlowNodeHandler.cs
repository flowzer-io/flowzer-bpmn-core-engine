namespace core_engine.Handler;

public interface IFlowNodeHandler
{
    public Variables? Execute(Variables? inputData = null);
}