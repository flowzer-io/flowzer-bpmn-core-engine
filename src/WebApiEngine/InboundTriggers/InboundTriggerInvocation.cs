using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Model;
using StorageSystem;
using StorageSystem.Exceptions;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Idempotency;

namespace WebApiEngine.InboundTriggers;

/// <summary>Wie ein Aufruf von außen ausgegangen ist.</summary>
public enum InboundTriggerOutcome
{
    /// <summary>Eine Instanz wurde gestartet.</summary>
    Started,

    /// <summary>Die Nachricht wurde zugestellt oder hat eine Instanz gestartet.</summary>
    Correlated,

    /// <summary>Keine Instanz hat gewartet. Ausdrücklich kein Fehler.</summary>
    NotCorrelated,

    /// <summary>Signatur fehlt, stimmt nicht, oder der Zeitstempel liegt außerhalb des Fensters.</summary>
    Unauthorized,

    /// <summary>Unbekannter oder abgeschalteter Schlüssel — von außen ununterscheidbar.</summary>
    NotFound,

    /// <summary>Der Körper ist kein JSON-Objekt oder der Korrelationspfad steht nicht darin.</summary>
    BadRequest,

    /// <summary>Der Workflow hat keine deployte Version, die sich starten ließe.</summary>
    NotDeployed
}

/// <summary>
/// Das Ergebnis eines Aufrufs. <see cref="Message"/> steht nur bei
/// <see cref="InboundTriggerOutcome.BadRequest"/> und <see cref="InboundTriggerOutcome.NotDeployed"/>
/// und nennt ausschließlich, was am Aufruf oder am Ziel nicht passt.
/// </summary>
public sealed record InboundTriggerResult(
    InboundTriggerOutcome Outcome,
    Guid? InstanceId = null,
    string? Message = null);

/// <summary>
/// Der Aufruf eines Auslösers von außen: prüfen, übersetzen, ausführen, zählen.
///
/// Die Reihenfolge ist Absicht und wird von billig nach teuer durchlaufen: Schlüssel,
/// Zeitstempel, Signatur — und erst danach JSON und Engine. Ein Aufrufer ohne gültige Signatur
/// soll weder den Parser noch die Engine beschäftigen. Die Größe des Körpers begrenzt bereits
/// der Controller, bevor überhaupt nachgesehen wird, ob es den Schlüssel gibt.
/// </summary>
public sealed class InboundTriggerInvocation(
    ITransactionalStorageProvider storageProvider,
    BpmnBusinessLogic bpmnBusinessLogic,
    IOptions<InboundTriggerOptions> options,
    TimeProvider timeProvider)
{
    public async Task<InboundTriggerResult> InvokeAsync(
        string key,
        string rawBody,
        string? timestampHeader,
        string? signatureHeader,
        IdempotencyRequestFactory idempotency)
    {
        var trigger = await LoadAsync(key);

        // Unbekannt und abgeschaltet antworten gleich: Sonst verriete die Antwort, dass es
        // diesen Schlüssel gibt, und ein abgeschalteter Auslöser wäre als Ziel erkennbar.
        if (trigger is null) return new InboundTriggerResult(InboundTriggerOutcome.NotFound);
        if (!trigger.Enabled)
        {
            await RecordFailureAsync(trigger.Id, "disabled");
            return new InboundTriggerResult(InboundTriggerOutcome.NotFound);
        }

        if (!TryReadTimestamp(timestampHeader, out var timestamp) || !IsWithinTolerance(timestamp))
        {
            await RecordFailureAsync(trigger.Id, "timestamp");
            return new InboundTriggerResult(InboundTriggerOutcome.Unauthorized);
        }

        if (!SignatureIsValid(trigger, timestamp, rawBody, signatureHeader))
        {
            await RecordFailureAsync(trigger.Id, "signature");
            return new InboundTriggerResult(InboundTriggerOutcome.Unauthorized);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
        }
        catch (JsonException)
        {
            await RecordFailureAsync(trigger.Id, "payload");
            return new InboundTriggerResult(InboundTriggerOutcome.BadRequest, Message: "The body must be JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                await RecordFailureAsync(trigger.Id, "payload");
                return new InboundTriggerResult(
                    InboundTriggerOutcome.BadRequest, Message: "The body must be a JSON object.");
            }

            var body = string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody;
            var result = trigger.Kind == InboundTriggerKind.Start
                ? await StartAsync(trigger, body, idempotency)
                : await CorrelateAsync(trigger, body, document.RootElement);

            if (result.Outcome is InboundTriggerOutcome.Started
                or InboundTriggerOutcome.Correlated
                or InboundTriggerOutcome.NotCorrelated)
            {
                // Auch eine Nachricht ohne wartende Instanz ist ein erfolgreicher Aufruf: Der
                // Auslöser hat getan, wozu er da ist. Wer im Betrieb sucht, warum nichts
                // passiert, sieht am Zähler, dass das fremde System überhaupt ankommt.
                await RecordUseAsync(trigger.Id);
            }

            return result;
        }
    }

    private async Task<InboundTriggerResult> StartAsync(
        InboundTrigger trigger,
        string rawBody,
        IdempotencyRequestFactory idempotency)
    {
        var variables = InboundTriggerPayload.Build(trigger.VariablesMode, trigger.AllowedFields, rawBody);
        try
        {
            var instance = await bpmnBusinessLogic.StartProcessInstance(
                trigger.DefinitionId!,
                variables,
                initiator: InboundTriggerIdentity.Subject(trigger),
                idempotency: idempotency(
                    InboundTriggerIdentity.Actor(trigger), "workflow-start", trigger.DefinitionId!, variables));
            return new InboundTriggerResult(InboundTriggerOutcome.Started, instance.InstanceId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DefinitionStorageNotFoundException)
        {
            await RecordFailureAsync(trigger.Id, "not-deployed");
            return new InboundTriggerResult(InboundTriggerOutcome.NotDeployed, Message: exception.Message);
        }
    }

    private async Task<InboundTriggerResult> CorrelateAsync(
        InboundTrigger trigger,
        string rawBody,
        JsonElement root)
    {
        var correlationKey = InboundTriggerPayload.ReadPath(root, trigger.CorrelationKeyPath!);
        if (string.IsNullOrEmpty(correlationKey))
        {
            await RecordFailureAsync(trigger.Id, "correlation-key");
            return new InboundTriggerResult(
                InboundTriggerOutcome.BadRequest,
                Message: $"The body has no single value at \"{trigger.CorrelationKeyPath}\".");
        }

        // Die Engine stellt eine Nachricht nur einer benannten Instanz zu: Ihre Suche nach der
        // Anmeldung vergleicht auch die Instanzkennung, und ein Aufrufer von außen kennt sie
        // nicht. Deshalb wird die wartende Instanz hier über Name und Korrelationsschlüssel
        // gesucht und der Engine mitgegeben.
        var waitingInstanceId = await FindWaitingInstanceAsync(trigger.MessageName!, correlationKey);
        if (waitingInstanceId is null)
        {
            // Bewusst kein Fehler: Eine Fehlermeldung wäre für einen Aufrufer ein Weg
            // herauszufinden, welche Vorgänge es in dieser Installation gibt.
            return new InboundTriggerResult(InboundTriggerOutcome.NotCorrelated);
        }

        var variables = InboundTriggerPayload.Build(trigger.VariablesMode, trigger.AllowedFields, rawBody);
        var message = new Model.Message
        {
            Name = trigger.MessageName!,
            CorrelationKey = correlationKey,
            Variables = Newtonsoft.Json.JsonConvert.SerializeObject(variables),
            InstanceId = waitingInstanceId
        };

        try
        {
            await bpmnBusinessLogic.HandleMessage(message);
            return new InboundTriggerResult(InboundTriggerOutcome.Correlated);
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException)
        {
            // Die Anmeldung war zwischen Suche und Zustellung fort — die Instanz ist inzwischen
            // weitergelaufen. Für den Aufrufer ist das derselbe Fall wie „wartet niemand".
            return new InboundTriggerResult(InboundTriggerOutcome.NotCorrelated);
        }
    }

    /// <summary>
    /// Sucht die Instanz, die auf diese Nachricht mit diesem Korrelationsschlüssel wartet.
    ///
    /// Anmeldungen ohne Instanz gehören zu Nachrichten-Startereignissen. Sie bleiben hier außen
    /// vor: Ihr Korrelationsschlüssel lässt sich vor dem Start nicht auflösen, und einen
    /// Workflow zu starten ist die Aufgabe eines Auslösers der Art <c>start</c>.
    /// </summary>
    private async Task<Guid?> FindWaitingInstanceAsync(string messageName, string correlationKey)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        var subscriptions = await storage.SubscriptionStorage.GetAllMessageSubscriptions();
        return subscriptions
            .Where(subscription =>
                subscription.ProcessInstanceId is not null
                && string.Equals(subscription.Message.Name, messageName, StringComparison.Ordinal)
                && string.Equals(subscription.Message.FlowzerCorrelationKey, correlationKey, StringComparison.Ordinal))
            .Select(subscription => subscription.ProcessInstanceId)
            .FirstOrDefault();
    }

    private bool IsWithinTolerance(long timestamp)
    {
        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        return Math.Abs(now - timestamp) <= (long)InboundTriggerSecret.Tolerance.TotalSeconds;
    }

    /// <summary>
    /// Öffnet das versiegelte Geheimnis, bildet die erwartete Signatur und vergleicht sie
    /// konstant-zeitig. Lässt sich das Siegel nicht öffnen — kein oder ein anderer
    /// installationsweiter Schlüssel —, gilt der Aufruf als nicht ausgewiesen.
    /// </summary>
    private bool SignatureIsValid(InboundTrigger trigger, long timestamp, string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader)) return false;

        var installationKey = options.Value.ResolveKey();
        if (installationKey.Length == 0) return false;
        if (!InboundTriggerSecret.TryUnprotect(trigger.SecretHash, trigger.Id, installationKey, out var secret))
        {
            return false;
        }

        var expected = InboundTriggerSecret.ComputeSignature(secret, timestamp, rawBody);
        return InboundTriggerSecret.SignatureMatches(expected, signatureHeader);
    }

    private async Task<InboundTrigger?> LoadAsync(string key)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        return await storage.InboundTriggerStorage.GetByKey(key);
    }

    private async Task RecordUseAsync(Guid id)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await storage.InboundTriggerStorage.RecordUse(id, timeProvider.GetUtcNow().UtcDateTime);
        storage.CommitChanges();
    }

    /// <summary>
    /// Vermerkt eine Ablehnung mit einem festen Grund. Daten des Aufrufers gehören nicht dazu:
    /// Sonst wäre ein offener Endpunkt ein Weg, beliebigen Text in die Ablage zu schreiben.
    /// </summary>
    private async Task RecordFailureAsync(Guid id, string reason)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await storage.InboundTriggerStorage.RecordFailure(id, timeProvider.GetUtcNow().UtcDateTime, reason);
        storage.CommitChanges();
    }

    private static bool TryReadTimestamp(string? header, out long timestamp) =>
        long.TryParse(header, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out timestamp);
}

/// <summary>
/// Erzeugt den Idempotenzvertrag eines Aufrufs. Als Delegat übergeben, damit die Auswertung des
/// HTTP-Headers im Controller bleibt und diese Klasse ohne <c>HttpContext</c> prüfbar ist.
/// </summary>
public delegate IdempotencyRequest? IdempotencyRequestFactory(
    CurrentUserContext actor, string operation, string resource, object? payload);
