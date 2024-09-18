namespace FlowzerFrontend.Models;

public class RestExampleRequest
{
    public required string Url{ get; set; }
    public required string Body{ get; set; }
    public HttpMethod Method { get; set; } = HttpMethod.Post;
}