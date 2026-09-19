using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MimeKit;
using StorageSystem;
using WebApiEngine.Connectors;
using Model;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Tests;

/// <summary>
/// Gemeinsame Testdoppel der mitgelieferten Konnektoren: ein Antwortgeber statt eines echten
/// Servers, ein Versandweg statt eines SMTP-Servers und eine Ablage ohne Engine.
/// </summary>
internal static class ConnectorTestData
{
    /// <summary>
    /// Freigegebenes Ziel als IP-Literal. Ein Name wuerde die SSRF-Pruefung in eine echte
    /// DNS-Abfrage schicken; der Test haengt dann am Netz statt am Konnektor.
    /// </summary>
    public const string AllowedHost = "93.184.216.34";

    public const string AllowedUrl = "https://93.184.216.34/api";

    public static Variables Inputs(params (string Name, object? Value)[] entries)
    {
        Variables variables = new();
        var writable = (IDictionary<string, object?>)variables;
        foreach (var entry in entries)
        {
            writable[entry.Name] = entry.Value;
        }

        return variables;
    }

    public static IDictionary<string, object?> Read(Variables? variables) =>
        variables is null ? new Dictionary<string, object?>() : (IDictionary<string, object?>)variables;

    public static ServiceTaskJob Job(string type, Variables? variables, int retries = 3) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Name = type,
        TokenId = Guid.NewGuid(),
        FlowNodeId = "ServiceTask_1",
        ProcessInstanceId = Guid.NewGuid(),
        MetaDefinitionId = "Definitions_Connector",
        DefinitionId = Guid.NewGuid(),
        ProcessId = "Process_Connector",
        Retries = retries,
        Variables = variables
    };
}

/// <summary>Beantwortet jede Anfrage nach Skript und haelt fest, was gesendet wurde.</summary>
internal sealed class RecordingConnectorHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }
    public AuthenticationHeaderValue? Authorization { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Request = request;
        Authorization = request.Headers.Authorization;
        RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return respond(request);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/plain")
    };
}

/// <summary>Wartet, bis der Konnektor selbst abbricht; so entsteht ein echter Zeitablauf.</summary>
internal sealed class NeverAnsweringHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }
}

/// <summary>Reicht genau einen Handler durch; der Konnektor holt sich den Client benannt.</summary>
internal sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
}

/// <summary>Nimmt Nachrichten entgegen, statt sie zu versenden — oder scheitert auf Ansage.</summary>
internal sealed class FakeSmtpSender(Exception? failure = null) : ISmtpSender
{
    public List<MimeMessage> Sent { get; } = [];

    public Task<SmtpSendResult> Send(MimeMessage message, CancellationToken cancellationToken)
    {
        if (failure is not null)
        {
            throw failure;
        }

        Sent.Add(message);
        return Task.FromResult(new SmtpSendResult(
            message.MessageId ?? string.Empty,
            message.To.Concat(message.Cc).Concat(message.Bcc)
                .OfType<MailboxAddress>()
                .Select(address => address.Address)
                .ToList()));
    }
}

/// <summary>Reicht dieselbe Auftragsablage durch; Konnektortests brauchen keine Transaktion.</summary>
internal sealed class ServiceTaskOnlyProvider(IServiceTaskStorage serviceTaskStorage) : ITransactionalStorageProvider
{
    public ITransactionalStorage GetTransactionalStorage() => new Wrapper(serviceTaskStorage);

    private sealed class Wrapper(IServiceTaskStorage serviceTaskStorage) : ITransactionalStorage
    {
        public IDefinitionStorage DefinitionStorage => throw new NotSupportedException();
        public IFolderStorage FolderStorage => throw new NotSupportedException();
        public IMessageSubscriptionStorage SubscriptionStorage => throw new NotSupportedException();
        public IInstanceStorage InstanceStorage => throw new NotSupportedException();
        public IFormStorage FormStorage => throw new NotSupportedException();
        public IServiceTaskStorage ServiceTaskStorage { get; } = serviceTaskStorage;

        public void CommitChanges()
        {
        }

        public void RollbackTransaction()
        {
        }

        public void Dispose()
        {
        }
    }
}
