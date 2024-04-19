using Common;

namespace Gateways;

public class ParallelGateway(string name, IFlowElementContainer container) : Gateway(name, container);