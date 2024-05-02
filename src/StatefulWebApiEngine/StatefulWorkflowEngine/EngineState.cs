using core_engine;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class EngineState
{
    public List<DefinitionsInfo> Models { get; } = [];
    public List<ProcessDefinition> ProcessDefinitions { get; } = [];
    public List<MessageSubscription> ActiveMessages { get; } = [];
}