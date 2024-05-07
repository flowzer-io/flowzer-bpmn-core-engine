namespace core_engine;

public enum ProcessInstanceState
{
    Initialized,
    Running,
    Waiting,
    Completing,
    Completed,
    Failing,
    Failed,
    Terminating,
    Terminated,
    Compensating,
    Compensated
}