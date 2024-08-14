namespace FlowzerFrontend;

public class UriHelper
{
    public static string GetEdotDefinitionUrl(string definitionId)
    {
        return $"/definition/{definitionId}";
    }
}