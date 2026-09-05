using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Model;
using StorageSystem;

namespace WebApiEngine.Jobs;

/// <summary>
/// Benachrichtigt angemeldete Worker, sobald ein Auftrag ihres Typs frei ist.
///
/// Die Benachrichtigung enthält bewusst nur Kennung, Typ und Entstehungszeit des Auftrags. Auch
/// die Instanzkennung fehlt: Sie ließe sich sonst über eine angemeldete Adresse mitschreiben,
/// ohne dass der Empfänger je einen Auftrag abholt.
/// </summary>
public sealed class ServiceTaskWebhookNotifier(
    ITransactionalStorageProvider storageProvider,
    ServiceTaskWebhookService webhookService,
    IHttpClientFactory httpClientFactory,
    FlowzerWebhookOptions options,
    TimeProvider timeProvider,
    ILogger<ServiceTaskWebhookNotifier> logger)
{
    public const string SignatureHeader = "X-Flowzer-Signature";
    public const string EventHeader = "X-Flowzer-Event";
    public const string JobAvailableEvent = "service-task.available";

    /// <summary>
    /// Bereits gemeldete Paare aus Anmeldung und Auftrag. Der Schluessel enthaelt die Anmeldung,
    /// nicht nur den Auftrag: Sonst bekaeme bei mehreren Workern desselben Typs nur der erste
    /// eine Benachrichtigung.
    /// </summary>
    private readonly HashSet<(Guid WebhookId, Guid JobId)> _announced = [];

    public async Task NotifyPendingJobs(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        List<ServiceTaskWebhook> webhooks;
        List<ServiceTaskJob> jobs;
        using (var storage = storageProvider.GetTransactionalStorage())
        {
            webhooks = (await storage.ServiceTaskStorage.GetWebhooks()).ToList();
            if (webhooks.Count == 0)
            {
                return;
            }

            jobs = (await storage.ServiceTaskStorage.GetJobs()).Where(job => job.IsAvailableAt(now)).ToList();
        }

        // Erledigte Auftraege aus der Merkliste nehmen, damit sie nicht unbegrenzt waechst und
        // eine erneute Vergabe desselben Auftrags nach einem Fehlschlag wieder gemeldet wird.
        var availableIds = jobs.Select(job => job.Id).ToHashSet();
        var webhookIds = webhooks.Select(webhook => webhook.Id).ToHashSet();
        _announced.RemoveWhere(entry => !availableIds.Contains(entry.JobId) || !webhookIds.Contains(entry.WebhookId));

        foreach (var webhook in webhooks)
        {
            if (webhook.ConsecutiveFailures >= options.MaxConsecutiveFailures)
            {
                continue;
            }

            var matching = jobs.Where(job =>
                string.Equals(job.Type, webhook.Type, StringComparison.Ordinal)
                && !_announced.Contains((webhook.Id, job.Id))).ToList();

            foreach (var job in matching)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (await Deliver(webhook, job, cancellationToken))
                {
                    _announced.Add((webhook.Id, job.Id));
                    continue;
                }

                // Ein unerreichbarer Worker soll nicht in einem einzigen Durchgang abgeschaltet
                // werden, nur weil gerade viele Auftraege vorliegen: Der Zaehler steht fuer
                // Durchgaenge, nicht fuer Auftraege.
                break;
            }
        }
    }

    private async Task<bool> Deliver(ServiceTaskWebhook webhook, ServiceTaskJob job, CancellationToken cancellationToken)
    {
        // Die Freigabeliste kann sich geaendert haben, seit der Webhook angemeldet wurde.
        var targetProblem = webhookService.ValidateTarget(webhook.Url);
        if (targetProblem is not null)
        {
            await RecordFailure(webhook, targetProblem);
            return false;
        }

        var payload = JsonSerializer.Serialize(new
        {
            @event = JobAvailableEvent,
            jobId = job.Id,
            type = job.Type,
            createdAt = job.CreatedAt
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(EventHeader, JobAvailableEvent);

        if (!string.IsNullOrEmpty(webhook.Secret))
        {
            request.Headers.TryAddWithoutValidation(SignatureHeader, ComputeSignature(webhook.Secret, payload));
        }

        try
        {
            using var client = httpClientFactory.CreateClient("flowzer-webhook");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await RecordFailure(webhook, $"HTTP {(int)response.StatusCode}");
                return false;
            }

            await RecordSuccess(webhook);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await RecordFailure(webhook, exception.Message);
            return false;
        }
    }

    /// <summary>
    /// HMAC-SHA256 über den Nachrichtentext. Der Worker bildet dieselbe Signatur und erkennt
    /// daran, dass die Benachrichtigung von dieser Installation stammt.
    /// </summary>
    public static string ComputeSignature(string secret, string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private async Task RecordSuccess(ServiceTaskWebhook webhook)
    {
        if (webhook.ConsecutiveFailures == 0 && webhook.LastError is null)
        {
            webhook.LastAttemptAt = timeProvider.GetUtcNow().UtcDateTime;
            return;
        }

        webhook.ConsecutiveFailures = 0;
        webhook.LastError = null;
        webhook.LastAttemptAt = timeProvider.GetUtcNow().UtcDateTime;
        await Persist(webhook);
    }

    private async Task RecordFailure(ServiceTaskWebhook webhook, string error)
    {
        webhook.ConsecutiveFailures++;
        webhook.LastError = error;
        webhook.LastAttemptAt = timeProvider.GetUtcNow().UtcDateTime;

        logger.LogWarning(
            "Benachrichtigung an {Url} fehlgeschlagen ({Failures}. Versuch in Folge): {Error}",
            webhook.Url, webhook.ConsecutiveFailures, error);

        await Persist(webhook);
    }

    private async Task Persist(ServiceTaskWebhook webhook)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await storage.ServiceTaskStorage.SaveWebhook(webhook);
        storage.CommitChanges();
    }
}
