using System.Text;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

public class FlowzerControllerBase: ControllerBase
{
    /// <summary>
    /// Ablehnung einer einzelnen Handlung, wenn die Berechtigung nicht an einer Anwendungsrolle
    /// haengt, sondern an den Daten — etwa an der Zustaendigkeit fuer einen Ordner. Setzt
    /// denselben Antwortheader wie die Ablehnung durch die Autorisierungsschicht, damit die
    /// Oberflaeche beide Faelle gleich behandeln kann und die Ablehnung nicht als kompletter
    /// Zugangsverlust erscheint.
    /// </summary>
    protected ActionResult<ApiStatusResult<T>> ForbiddenCapability<T>(string message)
    {
        Response.Headers[FlowzerPolicies.AccessDeniedHeader] = FlowzerPolicies.DeniedCapability;
        return StatusCode(StatusCodes.Status403Forbidden, new ApiStatusResult<T>(message));
    }

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
