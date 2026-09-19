using System.Net.Http.Headers;
using System.Text;
using WebApiEngine.Jobs;

namespace WebApiEngine.Connectors;

/// <summary>
/// Mitgelieferter Worker fuer HTTP-Aufrufe (<c>flowzer:http</c>).
///
/// Die Eingaben kommen wie bei jedem Service-Task aus den Auftragsvariablen; der bessere Weg
/// ist ein <c>zeebe:ioMapping</c>, damit am Modell ablesbar bleibt, was das Haus verlaesst.
/// Die Freigabeliste, das Verbot von Weiterleitungen und die Abweisung interner Adressen sind
/// dieselben Regeln wie bei den Worker-Webhooks: Ohne sie waere ein Modell eine Aufforderung
/// an die Engine, beliebige Adressen aufzurufen, auch die im eigenen Netz.
/// </summary>
public sealed class HttpConnector(
    IHttpClientFactory httpClientFactory,
    FlowzerConnectorOptions options,
    ConnectorSecretResolver secretResolver,
    ILogger<HttpConnector> logger) : IBuiltInConnector
{
    public const string HttpClientName = "flowzer-connector-http";
    public const string InvalidRequestErrorCode = "HTTP_INVALID_REQUEST";
    public const string NotAllowedErrorCode = "HTTP_NOT_ALLOWED";

    private static readonly string[] AllowedMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public string JobType => HttpConnectorOptions.JobType;

    public string Name => "http";

    public bool Enabled => options.Http.Enabled;

    public async Task<ConnectorOutcome> Execute(ServiceTaskJob job, CancellationToken cancellationToken)
    {
        var settings = options.Http;
        var inputs = ConnectorValues.Read(job.Variables);

        var rawUrl = ConnectorValues.AsString(ConnectorValues.Get(inputs, "url"));
        if (string.IsNullOrWhiteSpace(rawUrl) || !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var url))
        {
            return Invalid("A non-empty absolute 'url' is required.");
        }

        var methodName = (ConnectorValues.AsString(ConnectorValues.Get(inputs, "method")) ?? "GET")
            .Trim().ToUpperInvariant();
        if (!AllowedMethods.Contains(methodName, StringComparer.Ordinal))
        {
            return Invalid($"Method '{methodName}' is not supported.");
        }

        var targetProblem = DescribeDisallowedTarget(url, settings);
        if (targetProblem is not null)
        {
            return new ConnectorOutcome.BpmnError(
                NotAllowedErrorCode,
                targetProblem,
                ConnectorValues.Build(("url", url.GetLeftPart(UriPartial.Path))));
        }

        var requested = ConnectorValues.AsInteger(ConnectorValues.Get(inputs, "timeoutSeconds"));
        var timeout = TimeSpan.FromSeconds(requested is null
            ? settings.ResolvedTimeoutSeconds
            : Math.Clamp(requested.Value, 1, settings.ResolvedMaxTimeoutSeconds));

        var errorOn4xx = ConnectorValues.AsBoolean(ConnectorValues.Get(inputs, "errorOn4xx")) ?? true;

        string? authorization = null;
        var rawAuthorization = ConnectorValues.AsString(ConnectorValues.Get(inputs, "authorization"));
        if (!string.IsNullOrWhiteSpace(rawAuthorization))
        {
            if (ConnectorSecretResolver.IsReference(rawAuthorization))
            {
                authorization = secretResolver.Resolve(rawAuthorization);
                if (authorization is null)
                {
                    // Der Name der Referenz bleibt draussen: Er stammt aus dem Modell und
                    // saehe im Fehlerpfad wie ein Hinweis auf vorhandene Secrets aus.
                    return Invalid("The configured authorization secret could not be resolved.");
                }
            }
            else
            {
                authorization = rawAuthorization;
            }
        }

        using var request = BuildRequest(url, methodName, inputs, authorization);

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);

            var status = (int)response.StatusCode;
            var (body, truncated) = await ReadBody(response, settings.ResolvedMaxResponseBytes, timeoutSource.Token);

            if (status >= 500)
            {
                return new ConnectorOutcome.Failed(
                    $"{methodName} {url.GetLeftPart(UriPartial.Path)} answered HTTP {status}.",
                    settings.ResolvedRetryBackoff);
            }

            if (status >= 400 && errorOn4xx)
            {
                return new ConnectorOutcome.BpmnError(
                    $"HTTP_{status}",
                    $"HTTP {status} {response.ReasonPhrase}".TrimEnd(),
                    ConnectorValues.Build(("status", status), ("body", body)));
            }

            if (truncated)
            {
                logger.LogWarning(
                    "Antwort des HTTP-Konnektors fuer Auftrag {JobId} auf {MaxBytes} Byte gekuerzt.",
                    job.Id,
                    settings.ResolvedMaxResponseBytes);
            }

            return new ConnectorOutcome.Completed(ConnectorValues.Build(
                ("status", status),
                ("headers", ConnectorValues.ToVariables(ReadableHeaders(response))),
                ("body", body)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ConnectorOutcome.Failed(
                $"{methodName} {url.GetLeftPart(UriPartial.Path)} timed out after {timeout.TotalSeconds:0} s.",
                settings.ResolvedRetryBackoff);
        }
        catch (HttpRequestException exception)
        {
            // Die Adresse nur bis zum Pfad: Ein Query-Parameter kann ein Kennzeichen tragen,
            // und die Meldung landet am Auftrag und in der Betriebssicht.
            return new ConnectorOutcome.Failed(
                $"{methodName} {url.GetLeftPart(UriPartial.Path)} failed: {exception.Message}",
                settings.ResolvedRetryBackoff);
        }
    }

    /// <summary>
    /// Prueft das Ziel gegen Schema, Freigabeliste und das eigene Netz. Die Antwort ist eine
    /// Begruendung oder <c>null</c> fuer "erlaubt".
    /// </summary>
    private static string? DescribeDisallowedTarget(Uri url, HttpConnectorOptions settings)
    {
        if (url.Scheme != Uri.UriSchemeHttps && !(settings.AllowHttp && url.Scheme == Uri.UriSchemeHttp))
        {
            return "Url must use https.";
        }

        var allowedHosts = settings.ResolvedAllowedHosts;
        if (allowedHosts.Length == 0)
        {
            return "No connector target hosts are configured; ask the operator to allow the host.";
        }

        var host = url.Host;
        var allowed = allowedHosts.Any(pattern =>
            string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase)
            || (pattern.StartsWith("*.", StringComparison.Ordinal)
                && host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)));

        if (!allowed)
        {
            return $"Host '{host}' is not an allowed connector target.";
        }

        return ServiceTaskWebhookService.DescribeInternalTarget(url);
    }

    private static HttpRequestMessage BuildRequest(
        Uri url,
        string methodName,
        IDictionary<string, object?> inputs,
        string? authorization)
    {
        var request = new HttpRequestMessage(new HttpMethod(methodName), AppendQuery(url, inputs));
        var headers = ConnectorValues.AsTextPairs(ConnectorValues.Get(inputs, "headers"));

        var (content, isJson) = ConnectorValues.AsRequestBody(ConnectorValues.Get(inputs, "body"));
        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, isJson ? "application/json" : "text/plain");

            var declaredType = headers
                .FirstOrDefault(header => string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                .Value;
            if (declaredType is not null && MediaTypeHeaderValue.TryParse(declaredType, out var parsedType))
            {
                request.Content.Headers.ContentType = parsedType;
            }
        }

        foreach (var header in headers)
        {
            // Der eigene Eingabewert `authorization` gewinnt; sonst liesse sich die
            // Secret-Aufloesung mit einem Klartextheader umgehen.
            if (authorization is not null
                && string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return request;
    }

    private static Uri AppendQuery(Uri url, IDictionary<string, object?> inputs)
    {
        var query = ConnectorValues.AsTextPairs(ConnectorValues.Get(inputs, "query"));
        if (query.Count == 0)
        {
            return url;
        }

        var builder = new UriBuilder(url);
        var appended = string.Join(
            '&',
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? appended
            : builder.Query.TrimStart('?') + "&" + appended;
        return builder.Uri;
    }

    /// <summary>
    /// Liest hoechstens die erlaubte Menge und meldet, ob dahinter noch etwas lag. Ohne diese
    /// Grenze bestimmte das aufgerufene System, wie viel Speicher der Prozess belegt.
    /// </summary>
    private static async Task<(object? Body, bool Truncated)> ReadBody(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[maxBytes];
        var read = 0;
        while (read < maxBytes)
        {
            var chunk = await stream.ReadAsync(buffer.AsMemory(read, maxBytes - read), cancellationToken);
            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        var truncated = read == maxBytes && await stream.ReadAsync(new byte[1], cancellationToken) > 0;
        var text = Encoding.UTF8.GetString(buffer, 0, read);

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var looksLikeJson = mediaType is not null
            && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

        if (looksLikeJson && !truncated && ConnectorValues.TryParseJsonText(text, out var parsed))
        {
            return (parsed, false);
        }

        return (text, truncated);
    }

    /// <summary>Nur lesbare Textwerte; mehrfach gesetzte Header werden zusammengefasst.</summary>
    private static IEnumerable<KeyValuePair<string, string>> ReadableHeaders(HttpResponseMessage response) =>
        response.Headers
            .Concat(response.Content.Headers)
            .Select(header => new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));

    private static ConnectorOutcome Invalid(string message) =>
        new ConnectorOutcome.BpmnError(InvalidRequestErrorCode, message, null);
}
