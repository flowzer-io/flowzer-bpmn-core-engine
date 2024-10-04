using Microsoft.AspNetCore.Components;

namespace FlowzerFrontend.Pages;

public partial class RestDialog
{
    [Inject] public required FlowzerApi FlowzerApi { get; set; }
    
    [Parameter] public RestDialogParams Content { get; set; }

    public string? Result { get; set; }
    
    private async Task SendRequst()
    {
        var respContent = "";
        try
        {
            var resp = await FlowzerApi.GetJsonRequest(Content.RestExampleRequest.Url, Content.RestExampleRequest.Method, Content.RestExampleRequest.Body);
            respContent = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();
            Result = respContent;
        }
        catch (Exception e)
        {
            Result = "Error: " + e.Message + "\r\nResponse Body:\r\n" + respContent;
        }
    }

    
}