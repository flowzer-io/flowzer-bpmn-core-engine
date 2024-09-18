namespace WebApiEngine.Shared;

public enum ProcessInstanceStateDto
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