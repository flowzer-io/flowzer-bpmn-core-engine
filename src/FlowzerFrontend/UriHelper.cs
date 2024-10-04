namespace FlowzerFrontend;

public class UriHelper
{
    public static string GetEditDefinitionUrl(string definitionId)
    {
        return $"/definition/{definitionId}";
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
}