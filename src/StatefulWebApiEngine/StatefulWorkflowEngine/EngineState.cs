using core_engine;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class EngineState
{
    public List<DefinitionsInfo> Models { get; } = [];
    public List<ProcessDefinition> ProcessEngines { get; } = [];
    public List<MessageSubscription> ActiveMessages { get; } = [];
}