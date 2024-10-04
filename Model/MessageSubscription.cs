namespace Model;

public record MessageSubscription(
    MessageDefinition Message,
    string ProcessId, //The processIf within the xml definitions
    string RelatedDefinitionId, //The "parent" definitionId which are all the same for the same bpmn file in diffent versions
    Guid DefinitionId, //The definitionId that was deployed
    Guid? ProcessInstanceId = null //The instanceId that is waiting for the message
    );