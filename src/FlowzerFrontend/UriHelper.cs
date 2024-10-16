namespace FlowzerFrontend;

public class UriHelper
{
    public static string GetEditDefinitionUrl(string metaDefinitionId, Guid? definitionId = null)
    {
        return $"/definition/{metaDefinitionId}" + (definitionId.HasValue ? $"/{definitionId}" : "");
    }

    public static string GetShowInstanceUrl(Guid instanceId)
    {
        return $"/instance/{instanceId}";
    }

    public static string GetShowFormUrl(Guid formId)
    {
        return $"/forms/{formId}";
    }

    public static string GetNewFormUrl()
    {
        return $"/forms/create";
    }

    public static string GetFilloutFormUrl(Guid processInstanceId, Guid tokenId, string tokenCurrentFlowNodeId)
    {
        return $"/filloutform/{processInstanceId}/{tokenId}/{tokenCurrentFlowNodeId}";
    }
}