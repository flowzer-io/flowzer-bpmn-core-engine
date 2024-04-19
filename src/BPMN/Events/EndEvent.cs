using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public class EndEvent(string name, IFlowElementContainer container, InputSet inputSet) : ThrowEvent(name, container, inputSet);