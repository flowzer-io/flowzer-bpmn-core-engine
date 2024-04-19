using BPMN.Common;

namespace BPMN.HumanInteraction;

public class ManualTask(string name, IFlowElementContainer container) : Activities.Task(name, container);