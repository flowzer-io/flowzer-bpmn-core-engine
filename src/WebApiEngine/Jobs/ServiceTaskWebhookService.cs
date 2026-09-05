using System.Net;
using System.Net.Sockets;
using Model;
using StorageSystem;

namespace WebApiEngine.Jobs;

/// <summary>
/// Verwaltet die Webhook-Anmeldungen. Ein Worker, der nicht regelmäßig fragen möchte, meldet
/// hier eine Adresse an und wird benachrichtigt, sobald ein Auftrag seines Typs vorliegt.
/// Geholt und zurückgemeldet wird trotzdem über die normalen Endpunkte: Die Benachrichtigung
/// ist ein Hinweis, keine Zustellung des Auftrags.
/// </summary>
public sealed class ServiceTaskWebhookService(
    ITransactionalStorageProvider storageProvider,
    FlowzerWebhookOptions options,
    TimeProvider timeProvider)
{
    public async Task<(ServiceTaskWebhook? Webhook, string? Error)> Register(
        string type,
        string url,
        string? secret,
        string? description,
        Guid userId)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return (null, "Type is required.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return (null, "Url must be an absolute URI.");
        }

        var urlProblem = ValidateTarget(parsed);
        if (urlProblem is not null)
        {
            return (null, urlProblem);
        }

        using var storage = storageProvider.GetTransactionalStorage();

        // Eine Adresse je Typ genuegt; eine zweite Anmeldung derselben Adresse wuerde denselben
        // Worker doppelt benachrichtigen.
        var existing = (await storage.ServiceTaskStorage.GetWebhooks())
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Type, type, StringComparison.Ordinal)
                && candidate.Url == parsed);

        var webhook = existing ?? new ServiceTaskWebhook
        {
            Id = Guid.NewGuid(),
            Type = type,
            Url = parsed,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            CreatedBy = userId
        };

        // Ein leeres Geheimnis heisst "unveraendert", nicht "abschalten": `GET /job/webhook`
        // liefert es bewusst nicht zurueck, ein Lesen-und-Schreiben wuerde die Signatur sonst
        // still ausschalten. Zum Entfernen die Anmeldung loeschen und neu anlegen.
        if (!string.IsNullOrWhiteSpace(secret))
        {
            webhook.Secret = secret;
        }

        webhook.Description = description;

        // Eine erneute Anmeldung ist eine bewusste Handlung des Betriebs; sie gibt einer
        // abgeschalteten Adresse wieder eine Chance.
        webhook.ConsecutiveFailures = 0;
        webhook.LastError = null;

        await storage.ServiceTaskStorage.SaveWebhook(webhook);
        storage.CommitChanges();

        return (webhook, null);
    }

    public async Task<IReadOnlyList<ServiceTaskWebhook>> GetAll()
    {
        using var storage = storageProvider.GetTransactionalStorage();
        return (await storage.ServiceTaskStorage.GetWebhooks()).OrderBy(webhook => webhook.Type).ToList();
    }

    public async Task<bool> Remove(Guid webhookId)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        if (await storage.ServiceTaskStorage.GetWebhook(webhookId) is null)
        {
            return false;
        }

        await storage.ServiceTaskStorage.RemoveWebhook(webhookId);
        storage.CommitChanges();
        return true;
    }

    /// <summary>
    /// Prüft das Ziel gegen die erlaubten Adressen. Ohne diese Prüfung wäre die Anmeldung eine
    /// Aufforderung an die Engine, beliebige Adressen aufzurufen, auch interne.
    /// </summary>
    public string? ValidateTarget(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps && !(options.AllowHttp && url.Scheme == Uri.UriSchemeHttp))
        {
            return "Url must use https.";
        }

        if (options.AllowedHosts.Length == 0)
        {
            return "No webhook target hosts are configured; ask the operator to allow the worker host.";
        }

        var host = url.Host;
        var allowed = options.AllowedHosts.Any(pattern =>
            string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase)
            || (pattern.StartsWith("*.", StringComparison.Ordinal)
                && host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)));

        if (!allowed)
        {
            return $"Host '{host}' is not an allowed webhook target.";
        }

        return DescribeInternalTarget(url);
    }

    /// <summary>
    /// Lehnt Ziele im eigenen Netz ab. Ein freigegebener Name kann auf 127.0.0.1, auf den
    /// Metadatendienst der Cloud oder in das Containernetz zeigen; die Engine wuerde dann von
    /// innen Adressen aufrufen, die von aussen nicht erreichbar sind.
    /// </summary>
    public static string? DescribeInternalTarget(Uri url)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(url.Host, out var literal)
                ? [literal]
                : Dns.GetHostAddresses(url.DnsSafeHost);
        }
        catch (SocketException)
        {
            return $"Host '{url.Host}' could not be resolved.";
        }

        if (addresses.Length == 0)
        {
            return $"Host '{url.Host}' could not be resolved.";
        }

        var internalAddress = addresses.FirstOrDefault(IsInternal);
        return internalAddress is null ? null : $"Host '{url.Host}' resolves to the internal address {internalAddress}.";
    }

    private static bool IsInternal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv4MappedToIPv6 ? IsInternal(address.MapToIPv4()) : address.Equals(IPAddress.IPv6Any);
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            127 => true,
            0 => true,
            169 when octets[1] == 254 => true,          // Link-Local, darunter der Metadatendienst
            172 when octets[1] >= 16 && octets[1] <= 31 => true,
            192 when octets[1] == 168 => true,
            100 when octets[1] >= 64 && octets[1] <= 127 => true,  // Carrier-Grade NAT
            _ => false
        };
    }
}

/// <summary>Grenzen für ausgehende Benachrichtigungen.</summary>
public sealed class FlowzerWebhookOptions
{
    public const string SectionName = "ServiceTaskWebhooks";

    /// <summary>
    /// Erlaubte Zieladressen, optional mit führendem <c>*.</c> für eine Domain. Absichtlich
    /// leer als Standard: Ohne ausdrückliche Freigabe ruft die Engine keine fremde Adresse auf.
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>Nur für Worker ohne TLS im selben Netz; standardmäßig aus.</summary>
    public bool AllowHttp { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Nach so vielen Fehlversuchen in Folge wird nicht mehr benachrichtigt.</summary>
    public int MaxConsecutiveFailures { get; set; } = 10;

    public bool Enabled { get; set; } = true;
}
