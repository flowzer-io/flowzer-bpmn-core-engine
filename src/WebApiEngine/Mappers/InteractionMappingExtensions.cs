using System.Dynamic;
using BPMN.Common;
using Newtonsoft.Json;
using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>
/// Bündelt explizite Abbildungen für Nachrichten-, Formular- und User-Task-Daten.
/// </summary>
public static class InteractionMappingExtensions
{
    public static UserTaskResult ToModel(this UserTaskResultDto userTaskResultDto)
    {
        ArgumentNullException.ThrowIfNull(userTaskResultDto);

        return new UserTaskResult
        {
            FlowNodeId = userTaskResultDto.FlowNodeId,
            TokenId = userTaskResultDto.TokenId,
            ProcessInstanceId = userTaskResultDto.ProcessInstanceId,
            Data = userTaskResultDto.Data
        };
    }

    public static MessageDto ToDto(this Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new MessageDto
        {
            Name = message.Name,
            CorrelationKey = message.CorrelationKey,
            Variables = DeserializeVariables(message.Variables),
            TimeToLive = message.TimeToLive,
            InstanceId = message.InstanceId
        };
    }

    public static Message ToModel(this MessageDto messageDto)
    {
        ArgumentNullException.ThrowIfNull(messageDto);

        return new Message
        {
            Name = messageDto.Name,
            CorrelationKey = messageDto.CorrelationKey,
            Variables = messageDto.Variables == null
                ? null
                : JsonConvert.SerializeObject(messageDto.Variables),
            TimeToLive = messageDto.TimeToLive,
            InstanceId = messageDto.InstanceId
        };
    }

    public static MessageDefinitionDto ToDto(this MessageDefinition messageDefinition)
    {
        ArgumentNullException.ThrowIfNull(messageDefinition);

        return new MessageDefinitionDto
        {
            Name = messageDefinition.Name,
            FlowzerId = messageDefinition.FlowzerId,
            FlowzerCorrelationKey = messageDefinition.FlowzerCorrelationKey
        };
    }

    public static MessageSubscriptionDto ToDto(this MessageSubscription messageSubscription)
    {
        ArgumentNullException.ThrowIfNull(messageSubscription);

        return new MessageSubscriptionDto
        {
            Message = messageSubscription.Message.ToDto(),
            ProcessId = messageSubscription.ProcessId,
            RelatedDefinitionId = messageSubscription.RelatedDefinitionId,
            DefinitionId = messageSubscription.DefinitionId,
            ProcessInstanceId = messageSubscription.ProcessInstanceId
        };
    }

    public static SignalSubscriptionDto ToDto(this SignalSubscription signalSubscription)
    {
        ArgumentNullException.ThrowIfNull(signalSubscription);

        return new SignalSubscriptionDto
        {
            Signal = signalSubscription.Signal,
            ProcessId = signalSubscription.ProcessId,
            RelatedDefinitionId = signalSubscription.RelatedDefinitionId,
            DefinitionId = signalSubscription.DefinitionId,
            ProcessInstanceId = signalSubscription.ProcessInstanceId
        };
    }

    public static FormDto ToDto(this Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        return new FormDto
        {
            Id = form.Id,
            FormId = form.FormId,
            Version = form.Version.ToDto(),
            FormData = form.FormData
        };
    }

    public static Form ToModel(this FormDto formDto)
    {
        ArgumentNullException.ThrowIfNull(formDto);

        if (string.IsNullOrWhiteSpace(formDto.FormData))
        {
            throw new ArgumentException("FormData is required.", nameof(formDto));
        }

        return new Form
        {
            Id = formDto.Id ?? Guid.Empty,
            FormId = formDto.FormId,
            Version = formDto.Version.ToModel(),
            FormData = formDto.FormData
        };
    }

    public static FormMetaDataDto ToDto(this FormMetadata formMetadata)
    {
        ArgumentNullException.ThrowIfNull(formMetadata);

        return new FormMetaDataDto
        {
            FormId = formMetadata.FormId,
            Name = formMetadata.Name
        };
    }

    public static FormMetadata ToModel(this FormMetaDataDto formMetadataDto)
    {
        ArgumentNullException.ThrowIfNull(formMetadataDto);

        return new FormMetadata
        {
            FormId = formMetadataDto.FormId,
            Name = formMetadataDto.Name
        };
    }

    private static ExpandoObject? DeserializeVariables(string? serializedVariables)
    {
        if (string.IsNullOrWhiteSpace(serializedVariables))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<ExpandoObject>(
            serializedVariables,
            new Newtonsoft.Json.Converters.ExpandoObjectConverter());
    }
}
