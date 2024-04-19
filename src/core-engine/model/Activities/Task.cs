using Common;

namespace Activities;

public abstract class Task(string name, IFlowElementContainer container) : Activity(name, container);