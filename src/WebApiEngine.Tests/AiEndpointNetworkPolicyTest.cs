using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Model;
using WebApiEngine.Ai;

namespace WebApiEngine.Tests;

/// <summary>Netzwerkgrenze fuer administrativ gebundene OpenAI-kompatible Endpunkte.</summary>
public sealed class AiEndpointNetworkPolicyTest
{
    // Testzweck: Ein Cloud-Endpunkt wird genau einmal aufgeloest und nur dann freigegeben,
    // wenn jede gelieferte Adresse oeffentlich routbar ist.
    [Test]
    public async Task Resolver_ShouldAcceptOnlyPublicCloudAddresses()
    {
        var addresses = new[]
        {
            IPAddress.Parse("8.8.8.8"),
            IPAddress.Parse("2606:4700:4700::1111"),
            IPAddress.Parse("8.8.8.8")
        };
        var dns = new FakeHostAddressResolver(addresses);
        var resolver = new AiResolvedEndpointResolver(dns);

        var resolved = await resolver.ResolveAsync(Connection(AiProcessingLocation.Cloud), default);

        dns.Calls.Should().Be(1);
        dns.LastHost.Should().Be("models.example.test");
        resolved.Host.Should().Be("models.example.test");
        resolved.Port.Should().Be(443);
        resolved.Addresses.Should().Equal(addresses.Take(2));
    }

    // Testzweck: Private, lokale, reservierte oder gemischte DNS-Antworten duerfen einen
    // als Cloud deklarierten Endpunkt nicht in interne Netze umleiten.
    [TestCase("10.1.2.3")]
    [TestCase("100.64.0.1")]
    [TestCase("127.0.0.1")]
    [TestCase("169.254.1.1")]
    [TestCase("172.16.0.1")]
    [TestCase("192.0.2.1")]
    [TestCase("192.168.1.1")]
    [TestCase("198.51.100.1")]
    [TestCase("203.0.113.1")]
    [TestCase("224.0.0.1")]
    [TestCase("240.0.0.1")]
    [TestCase("0.0.0.0")]
    [TestCase("::")]
    [TestCase("::1")]
    [TestCase("fc00::1")]
    [TestCase("fe80::1")]
    [TestCase("ff02::1")]
    [TestCase("2001:db8::1")]
    public async Task Resolver_ShouldRejectNonPublicCloudAddresses(string unsafeAddress)
    {
        var resolver = new AiResolvedEndpointResolver(new FakeHostAddressResolver(
        [
            IPAddress.Parse("8.8.8.8"),
            IPAddress.Parse(unsafeAddress)
        ]));

        var action = () => resolver.ResolveAsync(Connection(AiProcessingLocation.Cloud), default).AsTask();

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.connection.target_not_public");
        exception.Retryable.Should().BeFalse();
        exception.Message.Should().NotContain("models.example.test");
        exception.Message.Should().NotContain(unsafeAddress);
    }

    // Testzweck: Nur eine ausdruecklich lokale Verbindung darf private oder Loopback-Ziele
    // verwenden; eine literale Adresse benoetigt dabei keine zweite DNS-Aufloesung.
    [Test]
    public async Task Resolver_ShouldAllowPrivateAddressOnlyForLocalConnection()
    {
        var dns = new FakeHostAddressResolver([]);
        var resolver = new AiResolvedEndpointResolver(dns);
        var connection = Connection(AiProcessingLocation.Local) with
        {
            BaseAddress = "http://127.0.0.1:11434/v1"
        };

        var resolved = await resolver.ResolveAsync(connection, default);

        dns.Calls.Should().Be(0);
        resolved.Addresses.Should().Equal(IPAddress.Loopback);
        resolved.Port.Should().Be(11434);
    }

    // Testzweck: Ein temporaerer DNS-Fehler wird als stabiler, wiederholbarer Fehler ohne
    // Offenlegung des Zielnamens klassifiziert.
    [Test]
    public async Task Resolver_ShouldClassifyDnsFailureWithoutTargetDetails()
    {
        var resolver = new AiResolvedEndpointResolver(new FakeHostAddressResolver(
            new SocketException((int)SocketError.HostNotFound)));

        var action = () => resolver.ResolveAsync(Connection(AiProcessingLocation.Cloud), default).AsTask();

        var exception = (await action.Should().ThrowAsync<AiProviderCallException>()).Which;
        exception.Code.Should().Be("ai.connection.resolve_failed");
        exception.Retryable.Should().BeTrue();
        exception.Message.Should().NotContain("models.example.test");
    }

    // Testzweck: Der Socketaufbau verwendet ausschliesslich die zuvor gebundenen Adressen;
    // ein nachtraeglicher Host- oder Portwechsel erreicht den Dialer nicht.
    [Test]
    public async Task Connector_ShouldDialOnlyPinnedAddressesAndRejectTargetChange()
    {
        var first = IPAddress.Parse("8.8.8.8");
        var second = IPAddress.Parse("1.1.1.1");
        var dialer = new FakeSocketDialer(first);
        var connector = new PinnedAiSocketConnector(dialer);
        var endpoint = new AiResolvedEndpoint("models.example.test", 443, [first, second]);

        var stream = await connector.ConnectAsync(
            new DnsEndPoint("models.example.test", 443),
            endpoint,
            default);
        var changedHost = () => connector.ConnectAsync(
            new DnsEndPoint("internal.example.test", 443),
            endpoint,
            default).AsTask();
        var changedPort = () => connector.ConnectAsync(
            new DnsEndPoint("models.example.test", 8443),
            endpoint,
            default).AsTask();

        stream.Should().BeSameAs(dialer.Stream);
        dialer.Attempts.Should().Equal(first, second);
        await changedHost.Should().ThrowAsync<HttpRequestException>();
        await changedPort.Should().ThrowAsync<HttpRequestException>();
        dialer.Attempts.Should().HaveCount(2);
    }

    private static AiConnection Connection(AiProcessingLocation location) => new(
        Guid.Parse("A1111111-1111-4111-8111-111111111111"),
        "Compatible provider",
        AiProviderKind.OpenAiCompatible,
        location,
        "https://models.example.test/v1",
        "model",
        "env:FLOWZER_AI_TEST",
        true,
        1,
        DateTimeOffset.Parse("2026-09-09T12:00:00Z"),
        Guid.Parse("A2222222-2222-4222-8222-222222222222"));

    private sealed class FakeHostAddressResolver : IAiHostAddressResolver
    {
        private readonly IReadOnlyList<IPAddress>? _addresses;
        private readonly Exception? _failure;

        public FakeHostAddressResolver(IReadOnlyList<IPAddress> addresses) => _addresses = addresses;
        public FakeHostAddressResolver(Exception failure) => _failure = failure;

        public int Calls { get; private set; }
        public string? LastHost { get; private set; }

        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastHost = host;
            return _failure is null
                ? ValueTask.FromResult(_addresses!)
                : ValueTask.FromException<IReadOnlyList<IPAddress>>(_failure);
        }
    }

    private sealed class FakeSocketDialer(IPAddress failingAddress) : IAiSocketDialer
    {
        public Stream Stream { get; } = new MemoryStream();
        public List<IPAddress> Attempts { get; } = [];

        public ValueTask<Stream> ConnectAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Attempts.Add(address);
            return address.Equals(failingAddress)
                ? ValueTask.FromException<Stream>(new SocketException((int)SocketError.ConnectionRefused))
                : ValueTask.FromResult(Stream);
        }
    }
}
