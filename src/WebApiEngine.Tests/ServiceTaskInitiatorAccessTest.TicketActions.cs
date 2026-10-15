using FluentAssertions;
using WebApiEngine.Jobs;

namespace WebApiEngine.Tests;

public sealed partial class ServiceTaskInitiatorAccessTest
{
    // Testzweck: Der bereits jobgebundene Beweis muss zusätzlich genau den
    // konfigurierten TT-API-Client prüfen, nicht nur Flowzer oder den Workerclient.
    [TestCase("tt.ticket.read")]
    [TestCase("tt.ticket.create")]
    [TestCase("tt.ticket.close")]
    [TestCase("tt.ticket.delegate")]
    public async Task Check_ShouldRequireBothInstalledApiClients(string type)
    {
        var context = new Context(type);
        var result = await context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", CancellationToken.None);
        result.Status.Should().Be(ServiceTaskInitiatorAccessStatus.Ok);
        result.Access!.Allowed.Should().BeTrue(); context.Reader.HostClients.Should().Equal("tt-api");
        context.Reader.Requests.Should().HaveCount(1); context.Provider.Commits.Should().Be(0);
    }

    // Testzweck: Fehlende, fremd normalisierte oder als Flowzer getarnte Hostbindung
    // bleibt 503 vor Storage-/Provider-I/O; eine funktionierende Flowzer-Rolle genügt nicht.
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("tt-api ")]
    [TestCase("tt/api")]
    [TestCase("tt%2Fapi")]
    [TestCase("flowzer-api")]
    [TestCase("tt\napi")]
    [TestCase(".")]
    [TestCase("..")]
    public async Task Check_ShouldRejectUnboundHostClientBeforeIo(string clientId)
    {
        var context = new Context(); context.TicketActions.ApiClientId = clientId;
        var result = await context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", CancellationToken.None);
        result.Status.Should().Be(ServiceTaskInitiatorAccessStatus.Unavailable); result.Access.Should().BeNull();
        context.Reader.Requests.Should().BeEmpty(); context.Provider.Openings.Should().Be(0);
    }

    // Testzweck: Ein neues positives Ergebnis nach einem persönlichen Entzug bleibt
    // nur ein aktueller Stand. Hier entsteht ausdrücklich keine automatische Wiederfreigabe.
    [Test]
    public async Task Check_ShouldReadBothClientsAgainWithoutReusingPreviousAccess()
    {
        var context = new Context(); context.Reader.Allowed = true;
        (await context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", CancellationToken.None)).Access!.Allowed.Should().BeTrue();
        context.Reader.Allowed = false;
        (await context.Service.CheckAsync(context.Job.Id, Worker, "worker-a", CancellationToken.None)).Access!.Allowed.Should().BeFalse();
        context.Reader.HostClients.Should().Equal("tt-api", "tt-api");
        context.Provider.Commits.Should().Be(0); context.Job.Retries.Should().Be(3);
    }
}
