using Common;
using Data;

namespace Events;

public class ImplicitThrowEvent(string name, IFlowElementContainer container, InputSet inputSet) 
    : ThrowEvent(name, container, inputSet);