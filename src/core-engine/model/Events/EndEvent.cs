using Common;
using Data;

namespace Events;

public class EndEvent(string name, IFlowElementContainer container, InputSet inputSet) : ThrowEvent(name, container, inputSet);