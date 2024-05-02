using BPMN_Model.Common;
using core_engine;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class EngineState
{
    public List<DefinitionsInfo> Models { get; } = [];
    public List<NotInstantiatedProcess> ProcessEngines { get; } = [];
    public List<MessageSubscription> ActiveMessages { get; } = [];
}