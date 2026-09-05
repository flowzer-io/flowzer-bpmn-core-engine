using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model;
using WebApiEngine.Auth;
using WebApiEngine.Jobs;
using WebApiEngine.Shared;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Controller;

/// <summary>
/// Aufträge für externe Worker. Ein Worker holt Aufträge eines Typs, arbeitet sie ab und meldet
/// Ergebnis oder Fehler zurück. Wer nicht fragen möchte, meldet einen Webhook an und wird
/// benachrichtigt, sobald ein Auftrag vorliegt.
/// </summary>
[ApiController, Route("job")]
// Auftraege tragen die Eingabewerte des Prozessschritts. Wer sie abholt, liest damit
// Prozessdaten; das ist eine eigene Rolle und nicht jede angemeldete Person.
[Authorize(Policy = FlowzerPolicies.Worker)]
public class JobController(
    ServiceTaskJobService jobService,
    ServiceTaskWebhookService webhookService,
    ICurrentUserContextAccessor currentUserContextAccessor) : FlowzerControllerBase
{
    /// <summary>Übernimmt bis zu <c>MaxJobs</c> Aufträge des angegebenen Typs.</summary>
    [HttpPost("fetch")]
    public async Task<ActionResult<ApiStatusResult<ServiceTaskJobDto[]>>> FetchJobs([FromBody] FetchJobsRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.WorkerId))
        {
            return BadRequest(new ApiStatusResult<ServiceTaskJobDto[]>("Type and WorkerId are required."));
        }

        if (request.MaxJobs is < 1 or > 100)
        {
            return BadRequest(new ApiStatusResult<ServiceTaskJobDto[]>("MaxJobs must be between 1 and 100."));
        }

        if (request.LockSeconds is < 1 or > 3600)
        {
            return BadRequest(new ApiStatusResult<ServiceTaskJobDto[]>("LockSeconds must be between 1 and 3600."));
        }

        var userId = currentUserContextAccessor.GetCurrentUser().RequireResolvedUserId("fetching service task jobs");
        var jobs = await jobService.FetchAndLock(
            request.Type,
            userId,
            request.WorkerId,
            request.MaxJobs,
            TimeSpan.FromSeconds(request.LockSeconds));

        return Ok(new ApiStatusResult<ServiceTaskJobDto[]>(jobs.Select(ToDto).ToArray()));
    }

    [HttpPost("{jobId:guid}/complete")]
    public async Task<ActionResult<ApiStatusResult>> CompleteJob(Guid jobId, [FromBody] CompleteJobRequestDto request)
    {
        var userId = currentUserContextAccessor.GetCurrentUser().RequireResolvedUserId("completing service tasks");
        var variables = request.Variables is null ? null : ToVariables(request.Variables);

        return Translate(await jobService.Complete(jobId, userId, request.WorkerId, variables));
    }

    [HttpPost("{jobId:guid}/fail")]
    public async Task<ActionResult<ApiStatusResult>> FailJob(Guid jobId, [FromBody] FailJobRequestDto request)
    {
        if (request.RetryBackoffSeconds is < 0 or > 86400)
        {
            return BadRequest(new ApiStatusResult("RetryBackoffSeconds must be between 0 and 86400."));
        }

        var userId = currentUserContextAccessor.GetCurrentUser().RequireResolvedUserId("failing service tasks");
        return Translate(await jobService.Fail(
            jobId,
            userId,
            request.WorkerId,
            request.ErrorMessage,
            request.Retries,
            TimeSpan.FromSeconds(request.RetryBackoffSeconds)));
    }

    /// <summary>Alle Aufträge samt Zustand; für die Betriebssicht auf hängende Arbeit.</summary>
    [HttpGet]
    [Authorize(Policy = FlowzerPolicies.Operator)]
    public async Task<ActionResult<ApiStatusResult<ServiceTaskJobDto[]>>> GetJobs()
    {
        var jobs = await jobService.GetAll();
        return Ok(new ApiStatusResult<ServiceTaskJobDto[]>(jobs.Select(ToDto).ToArray()));
    }

    [HttpGet("webhook")]
    // Eine Webhook-Anmeldung laesst die Engine eine fremde Adresse aufrufen; das entscheidet der Betrieb.
    [Authorize(Policy = FlowzerPolicies.Operator)]
    public async Task<ActionResult<ApiStatusResult<ServiceTaskWebhookDto[]>>> GetWebhooks()
    {
        var webhooks = await webhookService.GetAll();
        return Ok(new ApiStatusResult<ServiceTaskWebhookDto[]>(webhooks.Select(ToDto).ToArray()));
    }

    [HttpPost("webhook")]
    // Eine Webhook-Anmeldung laesst die Engine eine fremde Adresse aufrufen; das entscheidet der Betrieb.
    [Authorize(Policy = FlowzerPolicies.Operator)]
    public async Task<ActionResult<ApiStatusResult<ServiceTaskWebhookDto>>> RegisterWebhook([FromBody] ServiceTaskWebhookDto request)
    {
        var userId = currentUserContextAccessor.GetCurrentUser().RequireResolvedUserId("registering service task webhooks");

        var registration = await webhookService.Register(request.Type, request.Url, request.Secret, request.Description, userId);
        if (registration.Error is not null)
        {
            return BadRequest(new ApiStatusResult<ServiceTaskWebhookDto>(registration.Error));
        }

        return Ok(new ApiStatusResult<ServiceTaskWebhookDto>(ToDto(registration.Webhook!)));
    }

    [HttpDelete("webhook/{webhookId:guid}")]
    // Eine Webhook-Anmeldung laesst die Engine eine fremde Adresse aufrufen; das entscheidet der Betrieb.
    [Authorize(Policy = FlowzerPolicies.Operator)]
    public async Task<ActionResult<ApiStatusResult>> RemoveWebhook(Guid webhookId)
    {
        if (!await webhookService.Remove(webhookId))
        {
            return NotFound(new ApiStatusResult($"Webhook {webhookId} was not found."));
        }

        return Ok(new ApiStatusResult { Successful = true });
    }

    private ActionResult<ApiStatusResult> Translate(JobOperationResult result) => result switch
    {
        JobOperationResult.Ok => Ok(new ApiStatusResult { Successful = true }),
        JobOperationResult.NotFound => NotFound(new ApiStatusResult("The job was not found; it may already be finished.")),
        // Beides bedeutet: Der Auftrag gehoert gerade nicht diesem Worker. Ein 409 sagt genau das,
        // ohne zu verraten, wem er stattdessen gehoert.
        JobOperationResult.NotLockedByWorker => Conflict(new ApiStatusResult("The job is not locked by this worker.")),
        JobOperationResult.LockExpired => Conflict(new ApiStatusResult("The lock on this job has expired; the job was handed to another worker.")),
        _ => StatusCode(StatusCodes.Status500InternalServerError, new ApiStatusResult("Unexpected job operation result."))
    };

    private static ServiceTaskJobDto ToDto(ServiceTaskJob job) => new()
    {
        Id = job.Id,
        Type = job.Type,
        Name = job.Name,
        ProcessInstanceId = job.ProcessInstanceId,
        ProcessId = job.ProcessId,
        DefinitionId = job.DefinitionId,
        MetaDefinitionId = job.MetaDefinitionId,
        TokenId = job.Token.Id,
        FlowNodeId = job.Token.CurrentFlowNode?.Id,
        CreatedAt = job.CreatedAt,
        LockedUntil = job.LockedUntil,
        // Nur die Worker-Kennung, nicht die Person dahinter: Der Sperrinhaber enthaelt intern
        // beides, nach aussen genuegt der Name, unter dem der Worker sich gemeldet hat.
        LockedBy = job.LockedBy?.Split(':', 2).LastOrDefault(),
        Retries = job.Retries,
        RetryAt = job.RetryAt,
        LastErrorMessage = job.LastErrorMessage,
        Variables = job.Variables is null
            ? []
            : ((IDictionary<string, object?>)job.Variables).ToDictionary(entry => entry.Key, entry => entry.Value)
    };

    /// <summary>Das Geheimnis wird nie zurueckgeliefert; es steht nur beim Anmelden im Vertrag.</summary>
    private static ServiceTaskWebhookDto ToDto(ServiceTaskWebhook webhook) => new()
    {
        Id = webhook.Id,
        Type = webhook.Type,
        Url = webhook.Url.ToString(),
        Description = webhook.Description,
        CreatedAt = webhook.CreatedAt,
        ConsecutiveFailures = webhook.ConsecutiveFailures,
        LastAttemptAt = webhook.LastAttemptAt,
        LastError = webhook.LastError
    };

    private static Variables ToVariables(Dictionary<string, object?> source)
    {
        var variables = new Variables();
        var writable = (IDictionary<string, object?>)variables;
        foreach (var entry in source)
        {
            writable[entry.Key] = entry.Value;
        }

        return variables;
    }
}
