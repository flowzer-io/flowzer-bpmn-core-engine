using core_engine;
using FlowzerBPMN;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public record MessageSubscription(MessageDefinition MessageDefinition, ICatchHandler CatchHandler);