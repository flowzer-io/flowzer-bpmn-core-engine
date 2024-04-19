using BPMN.Common;

namespace BPMN.Activities;

public abstract class Task(string name, IFlowElementContainer container) : Activity(name, container);