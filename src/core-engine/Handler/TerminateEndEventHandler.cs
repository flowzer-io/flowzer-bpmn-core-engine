namespace core_engine.Handler;

public class TerminateEndEventHandler : DefaultFlowNodeHandler
{
    public override void Execute(ProcessInstance processInstance, Token token)
    {
        foreach (var tokenToTerminate in processInstance.Tokens.Where(x => 
                     x.State is 
                         FlowNodeState.Active or 
                         FlowNodeState.Completing or 
                         FlowNodeState.Compensating or
                         FlowNodeState.Ready or 
                         FlowNodeState.Failing))
        {
            tokenToTerminate.State = FlowNodeState.Terminating;
        }
        processInstance.State = ProcessInstanceState.Terminating;
    }
}