namespace WebApiEngine.Shared;

public enum FlowNodeStateDto
{
    Ready,
    Active,
    Completing,
    WaitingForLoopEnd,
    Completed,
    Failing,
    Terminating,
    Failed,
    Terminated,
    Withdrawn,
    Compensating,
    Compensated,
    Merged
}