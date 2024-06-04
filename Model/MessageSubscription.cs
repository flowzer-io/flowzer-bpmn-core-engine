using BPMN.Process;

namespace Model;

public record MessageSubscription(MessageDefinition Message, string ProcessId, Guid? ProcessInstanceId = null);