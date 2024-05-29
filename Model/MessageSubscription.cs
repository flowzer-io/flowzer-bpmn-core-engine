using BPMN.Process;

namespace Model;

public record MessageSubscription(MessageDefinition Message, Process Process, ProcessInstance? ProcessInstance = null);