using Common;

namespace HumanInteraction;

public class ManualTask(string name, IFlowElementContainer container) : Activities.Task(name, container);