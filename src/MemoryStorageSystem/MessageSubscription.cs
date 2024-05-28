using core_engine;
using Model;

namespace WebApiEngine.StatefulWorkflowEngine;

public record MessageSubscription(Message Message, ICatchHandler CatchHandler);