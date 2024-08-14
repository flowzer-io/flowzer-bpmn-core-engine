using System.Text;

namespace WebApiEngine.Controller;

public class FlowzerControllerBase: ControllerBase
{
    protected async Task<string> GetRawContent()
    {
        using var reader = new StreamReader(Request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var rawContent = await reader.ReadToEndAsync();
        return rawContent;
    }

}