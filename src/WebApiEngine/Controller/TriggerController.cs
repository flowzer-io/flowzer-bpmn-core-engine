using System.Text;
using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Idempotency;
using WebApiEngine.InboundTriggers;
using WebApiEngine.Middleware;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Die eine Adresse, unter der ein fremdes System einen Auslöser aufruft.
///
/// Ausdrücklich ohne Anmeldung: Ein Ticketsystem oder ein Formulardienst hat keine OIDC-Sitzung
/// und kein Bearer-Token. Ausgewiesen wird sich mit der Signatur über Zeitstempel und Körper,
/// wie bei Webhook-Anbietern üblich. Die Fallback-Policy der Installation verlangt sonst eine
/// Anmeldung, deshalb steht hier <see cref="AllowAnonymousAttribute"/>.
///
/// Die Antworten tragen bewusst nicht den Umschlag <see cref="ApiStatusResult{T}"/>: Ein fremdes
/// System soll <c>{ "instanceId": … }</c> beziehungsweise <c>{ "correlated": … }</c> lesen
/// können, ohne den Hausvertrag dieser API zu kennen.
/// </summary>
[ApiController]
[Route("trigger")]
[AllowAnonymous]
public sealed class TriggerController(InboundTriggerInvocation invocation) : ControllerBase
{
    [HttpPost("{key}")]
    [ProducesResponseType<InboundTriggerStartResultDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<InboundTriggerMessageResultDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Invoke(
        [FromRoute] string key,
        [FromHeader(Name = HttpIdempotency.HeaderName)] string? _idempotencyKey = null)
    {
        // Die Größe wird geprüft, bevor überhaupt nachgesehen wird, ob es diesen Schlüssel gibt.
        // Andernfalls verriete der Unterschied zwischen 413 und 404, welche Schlüssel existieren.
        var rawBody = await ReadBodyAsync();
        if (rawBody is null)
        {
            return Problem(
                StatusCodes.Status413PayloadTooLarge,
                $"The body must not exceed {InboundTriggerPayload.MaxBodyBytes} bytes.");
        }

        var result = await invocation.InvokeAsync(
            key,
            rawBody,
            Request.Headers[InboundTriggerSecret.TimestampHeader].FirstOrDefault(),
            Request.Headers[InboundTriggerSecret.SignatureHeader].FirstOrDefault(),
            (actor, operation, resource, payload) =>
                HttpIdempotency.Create(Request, actor, operation, resource, payload));

        return result.Outcome switch
        {
            InboundTriggerOutcome.Started =>
                Accepted(new InboundTriggerStartResultDto(result.InstanceId!.Value)),
            InboundTriggerOutcome.Correlated =>
                Accepted(new InboundTriggerMessageResultDto(true)),
            // Kein Fehler: Wartete keine Instanz, wäre eine Fehlermeldung für den Aufrufer ein
            // Weg herauszufinden, welche Vorgänge es in dieser Installation gibt.
            InboundTriggerOutcome.NotCorrelated =>
                Accepted(new InboundTriggerMessageResultDto(false)),
            InboundTriggerOutcome.Unauthorized =>
                Problem(StatusCodes.Status401Unauthorized, "The signature or the timestamp is not valid."),
            // Unbekannt und abgeschaltet antworten gleich.
            InboundTriggerOutcome.NotFound =>
                Problem(StatusCodes.Status404NotFound, "There is no trigger for this key."),
            InboundTriggerOutcome.NotDeployed =>
                Problem(StatusCodes.Status422UnprocessableEntity, result.Message ?? "The workflow cannot be started."),
            _ => Problem(StatusCodes.Status400BadRequest, result.Message ?? "The request could not be processed.")
        };
    }

    /// <summary>
    /// Liest den Körper als Text, höchstens <see cref="InboundTriggerPayload.MaxBodyBytes"/>
    /// Bytes. Liefert <c>null</c>, sobald mehr ankommt — gelesen wird gegen die Grenze, nicht
    /// erst hinterher gemessen: Ein Aufrufer soll den Speicher nicht füllen können, bevor die
    /// Grenze greift.
    /// </summary>
    private async Task<string?> ReadBodyAsync()
    {
        if (Request.ContentLength is { } declared && declared > InboundTriggerPayload.MaxBodyBytes) return null;

        var buffer = new byte[InboundTriggerPayload.MaxBodyBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await Request.Body.ReadAsync(buffer.AsMemory(read), HttpContext.RequestAborted);
            if (chunk == 0) break;
            read += chunk;
        }

        return read > InboundTriggerPayload.MaxBodyBytes ? null : Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>Fehlerantworten ohne Daten des Aufrufers und ohne Hinweis auf den Bestand.</summary>
    private ObjectResult Problem(int status, string detail)
    {
        var problem = new ApiProblemDetails
        {
            Status = status,
            Title = "The trigger call was rejected.",
            Detail = detail,
            Type = "about:blank",
            Instance = "/trigger"
        };
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }
}
