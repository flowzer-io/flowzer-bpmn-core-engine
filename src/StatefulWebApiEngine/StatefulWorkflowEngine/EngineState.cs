namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class EngineState
{
    public List<DefinitionsInfo> Models { get; } = [];
    public List<ProcessInfo> ProcessDefinitions { get; } = [];
    public List<MessageSubscription> ActiveMessages { get; } = [];
}