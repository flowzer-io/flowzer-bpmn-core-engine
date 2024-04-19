using Common;
using Data;

namespace Events;

public class StartEvent(string name, IFlowElementContainer container, OutputSet outputSet) : CatchEvent(name, container, outputSet);