using core_engine;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public record MessageSubscription(MessageDefinition MessageDefinition, ICatchHandler CatchHandler);