namespace core_engine.Handler;

public class TuNichtsHandler : IFlowNodeHandler
{
    public void Execute(ProcessInstance processInstance, Token token)
    {
        token.State = FlowNodeState.Completing;
        return;
    }
}