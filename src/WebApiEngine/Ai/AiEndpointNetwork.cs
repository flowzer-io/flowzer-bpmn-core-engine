using System.Net;
using System.Net.Sockets;
using Model;

namespace WebApiEngine.Ai;

/// <summary>Aufloesungsport fuer kontrollierbare DNS-Pruefungen ohne echten Netzzugriff im Test.</summary>
internal interface IAiHostAddressResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken);
}

internal sealed class SystemAiHostAddressResolver : IAiHostAddressResolver
{
    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, AddressFamily.Unspecified, cancellationToken);
}

/// <summary>
/// Ein unmittelbar vor dem Aufruf aufgeloestes Ziel. Die Adressen sind der einzige Vorrat,
/// aus dem der spaetere Socketaufbau waehlen darf; dadurch findet kein zweiter DNS-Lookup statt.
/// </summary>
internal sealed record AiResolvedEndpoint(
    string Host,
    int Port,
    IReadOnlyList<IPAddress> Addresses);

/// <summary>
/// Loest benutzerdefinierte Endpunkte innerhalb ihrer administrativ gewaehlten Netzwerkgrenze
/// auf. Cloudziele duerfen ausschliesslich oeffentliche Unicast-Adressen liefern.
/// </summary>
internal sealed class AiResolvedEndpointResolver(IAiHostAddressResolver resolver)
{
    public async ValueTask<AiResolvedEndpoint> ResolveAsync(
        AiConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Provider != AiProviderKind.OpenAiCompatible
            || !Enum.IsDefined(connection.Location)
            || !Uri.TryCreate(connection.BaseAddress, UriKind.Absolute, out var target)
            || target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
            throw Failure("ai.connection.invalid", retryable: false, "The AI connection target is invalid.");

        var host = target.IdnHost.Trim('[', ']');
        IReadOnlyList<IPAddress> resolved;
        if (IPAddress.TryParse(host, out var literal))
        {
            resolved = [literal];
        }
        else
        {
            try
            {
                resolved = await resolver.ResolveAsync(host, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SocketException)
            {
                throw Failure(
                    "ai.connection.resolve_failed",
                    retryable: true,
                    "The AI connection target could not be resolved.");
            }
        }

        var addresses = resolved
            .Select(Normalize)
            .Distinct()
            .ToArray();
        if (addresses.Length == 0)
            throw Failure(
                "ai.connection.resolve_failed",
                retryable: true,
                "The AI connection target could not be resolved.");

        if (connection.Location == AiProcessingLocation.Cloud)
        {
            if (addresses.Any(address => !IsPublicAddress(address)))
                throw Failure(
                    "ai.connection.target_not_public",
                    retryable: false,
                    "The AI cloud connection did not resolve exclusively to public addresses.");
        }
        else if (addresses.Any(address => !IsConnectableAddress(address)))
        {
            throw Failure(
                "ai.connection.target_invalid",
                retryable: false,
                "The AI local connection resolved to an invalid address.");
        }

        return new AiResolvedEndpoint(host, target.Port, addresses);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool IsConnectableAddress(IPAddress address) =>
        !address.Equals(IPAddress.Any)
        && !address.Equals(IPAddress.IPv6Any)
        && !address.Equals(IPAddress.None)
        && !address.Equals(IPAddress.IPv6None)
        && !address.IsIPv6Multicast
        && !(address.AddressFamily == AddressFamily.InterNetwork
             && (address.GetAddressBytes()[0] >= 224));

    private static bool IsPublicAddress(IPAddress address)
    {
        if (!IsConnectableAddress(address) || IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 0
                   && bytes[0] != 10
                   && bytes[0] != 127
                   && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                   && !(bytes[0] == 169 && bytes[1] == 254)
                   && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                   && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                   && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
                   && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
                   && !(bytes[0] == 192 && bytes[1] == 168)
                   && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                   && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                   && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                   && bytes[0] < 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast)
            return false;

        var ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xfe) != 0xfc
               // Dokumentationspraefix 2001:db8::/32 ist nicht global erreichbar.
               && !(ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0d && ipv6[3] == 0xb8);
    }

    private static AiProviderCallException Failure(string code, bool retryable, string message) =>
        new(code, retryable, message);
}

/// <summary>Schmaler Socketport, damit die Bindung an vorab gepruefte IPs testbar bleibt.</summary>
internal interface IAiSocketDialer
{
    ValueTask<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken);
}

internal sealed class SystemAiSocketDialer : IAiSocketDialer
{
    public async ValueTask<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>Verbindet nur mit dem exakt aufgeloesten Host, Port und Adressvorrat.</summary>
internal sealed class PinnedAiSocketConnector(IAiSocketDialer dialer)
{
    public async ValueTask<Stream> ConnectAsync(
        DnsEndPoint requested,
        AiResolvedEndpoint resolved,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(requested.Host, resolved.Host, StringComparison.OrdinalIgnoreCase)
            || requested.Port != resolved.Port)
            throw new HttpRequestException("The AI connection target changed after validation.");

        foreach (var address in resolved.Addresses)
        {
            try
            {
                return await dialer.ConnectAsync(address, resolved.Port, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SocketException)
            {
                // Keine Adresse in den Fehlertext uebernehmen; der naechste bereits gepruefte
                // Kandidat darf versucht werden, aber es findet keine neue Aufloesung statt.
            }
        }

        throw new HttpRequestException("The pinned AI connection target could not be reached.");
    }
}

internal interface IAiHttpClientLeaseFactory
{
    ValueTask<AiHttpClientLease> CreateAsync(
        AiConnection connection,
        CancellationToken cancellationToken);
}

/// <summary>Lebensdauer eines HTTP-Clients; geteilte Clients werden nicht mit entsorgt.</summary>
internal sealed class AiHttpClientLease(HttpClient client, bool ownsClient) : IDisposable
{
    public HttpClient Client { get; } = client;

    public void Dispose()
    {
        if (ownsClient) Client.Dispose();
    }
}

internal sealed class SharedAiHttpClientLeaseFactory(HttpClient client) : IAiHttpClientLeaseFactory
{
    public ValueTask<AiHttpClientLease> CreateAsync(
        AiConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AiHttpClientLease(client, ownsClient: false));
    }
}

/// <summary>
/// Erstellt fuer jeden benutzerdefinierten Aufruf einen Client, dessen ConnectCallback nur
/// die unmittelbar davor geprueften IP-Adressen verwendet. Systemproxys sind bewusst aus.
/// </summary>
internal sealed class PinnedAiHttpClientLeaseFactory(
    AiResolvedEndpointResolver resolver,
    PinnedAiSocketConnector connector) : IAiHttpClientLeaseFactory
{
    public async ValueTask<AiHttpClientLease> CreateAsync(
        AiConnection connection,
        CancellationToken cancellationToken)
    {
        var resolved = await resolver.ResolveAsync(connection, cancellationToken);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = (context, token) => connector.ConnectAsync(
                context.DnsEndPoint,
                resolved,
                token)
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        return new AiHttpClientLease(client, ownsClient: true);
    }
}
