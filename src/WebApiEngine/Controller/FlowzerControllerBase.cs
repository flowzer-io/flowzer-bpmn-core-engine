using System.Text;

namespace WebApiEngine.Controller;

public class FlowzerControllerBase: ControllerBase
{
    /// <summary>
    /// Obergrenze fuer hochgeladene BPMN-Definitionen. Ein Definitionsupload ist Text im
    /// Kilobyte-Bereich; alles darueber ist ein Fehler oder Missbrauch und wird als 413 abgelehnt,
    /// bevor der Body vollstaendig gelesen oder geparst wird.
    /// </summary>
    public const int MaxRawContentBytes = 4 * 1024 * 1024;

    protected async Task<string> GetRawContent()
    {
        if (Request.ContentLength is > MaxRawContentBytes)
        {
            throw new BadHttpRequestException(
                $"The request body exceeds the limit of {MaxRawContentBytes} bytes.",
                StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, HttpContext.RequestAborted)) > 0)
        {
            if (buffer.Length + read > MaxRawContentBytes)
            {
                throw new BadHttpRequestException(
                    $"The request body exceeds the limit of {MaxRawContentBytes} bytes.",
                    StatusCodes.Status413PayloadTooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
