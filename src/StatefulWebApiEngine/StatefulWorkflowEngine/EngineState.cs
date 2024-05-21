namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class EngineState
{
    public List<ProcessInfo> ProcessInfos { get; } = [];
    public List<MessageSubscription> ActiveMessages { get; } = [];
}