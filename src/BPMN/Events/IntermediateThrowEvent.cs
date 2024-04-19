using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public class IntermediateThrowEvent(string name, IFlowElementContainer container, InputSet inputSet) 
    : ThrowEvent(name, container, inputSet);