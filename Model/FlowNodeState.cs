namespace Model;

public enum FlowNodeState
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