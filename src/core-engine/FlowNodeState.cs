namespace core_engine;

public enum FlowNodeState
{
    Ready,
    Active,
    Completing,
    Completed,
    Failing,
    Terminating,
    Failed,
    Terminated,
    Withdrawn,
    Compensating,
    Compensated
}