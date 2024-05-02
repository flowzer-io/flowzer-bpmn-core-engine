using BPMN_Model.Common;
using core_engine;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class EngineState
{
    public List<Definitions> Models { get; } = [];
    public List<ProcessEngine> ProcessEngines { get; } = [];
    public List<MessageSubscription> ActiveMessages { get; } = [];
}