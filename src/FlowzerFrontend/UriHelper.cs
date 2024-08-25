namespace FlowzerFrontend;

public class UriHelper
{
    public static string GetEditDefinitionUrl(string definitionId)
    {
        return $"/definition/{definitionId}";
    }

    public static object GetShowInstanceUrl(Guid instanceId)
    {
        return $"/instance/{instanceId}";
    }
}