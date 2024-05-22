using core_engine;
using Model;

namespace WebApiEngine.StatefulWorkflowEngine;

public record MessageSubscription(MessageDefinition MessageDefinition, ICatchHandler CatchHandler);