using Common;
using Data;

namespace Events;

public class IntermediateThrowEvent(string name, IFlowElementContainer container, InputSet inputSet) 
    : ThrowEvent(name, container, inputSet);