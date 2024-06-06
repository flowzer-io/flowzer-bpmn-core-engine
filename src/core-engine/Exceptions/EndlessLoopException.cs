namespace core_engine.Exceptions;

public class EndlessLoopException(string? message = null) : FlowzerRuntimeException(message);