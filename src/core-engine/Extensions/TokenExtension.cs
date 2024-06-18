namespace core_engine.Extensions;

public static class TokenExtension
{
    public static bool IsAlive(this Token token)
    {
        return token.State is 
            not FlowNodeState.Completed and 
            not FlowNodeState.Terminated and 
            not FlowNodeState.Compensated and 
            not FlowNodeState.Withdrawn and 
            not FlowNodeState.Failed;
    }
}