namespace core_engine;

public enum ActivityState
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