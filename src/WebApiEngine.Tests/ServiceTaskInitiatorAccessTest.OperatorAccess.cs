using FluentAssertions;
using Model;
using WebApiEngine.Auth;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Jobs;

namespace WebApiEngine.Tests;

public sealed partial class ServiceTaskInitiatorAccessTest
{
    private static CurrentUserContext OperatorActor() => new(Worker, "jwt", false)
    { Identity = new(Issuer, "operator-person"), AuthorizedClientId = "tt-exchange" };
    private static TicketActionOperatorAccessService OperatorService(Context c)
    {
        c.Authentication.JwtBearer.Roles.Operator = "operator";
        return new(c.Provider, c.Reader, c.Authentication, c.Time, c.TicketActions);
    }

    // Testzweck: Die spätere Storage-Bindungsprüfung darf den tatsächlichen Live-Rollenzeitpunkt nicht künstlich verjüngen.
    [Test]
    public async Task OperatorProof_ShouldKeepActualRoleObservationTimeInsteadOfLaterReplyTime()
    {
        var c = new Context(); var service = OperatorService(c); var actualObservation = c.Time.GetUtcNow();
        c.Provider.BeforeInstanceRead = () => { if (c.Provider.Openings == 2) c.Time.Advance(TimeSpan.FromSeconds(2)); };
        var outcome = await service.CheckAsync(c.Instance.InstanceId, OperatorActor(), CancellationToken.None);
        outcome.Status.Should().Be(TicketActionOperatorAccessStatus.Ok);
        outcome.Access!.CheckedAtUtc.Should().Be(actualObservation); c.Time.GetUtcNow().Should().Be(actualObservation.AddSeconds(2));
        c.Provider.Commits.Should().Be(0);
    }

    // Testzweck: Genau die bedienende Person braucht aktuellen Flowzer- UND TT-API-Zugang und Betriebsrolle;
    // die gespeicherte Initiatoridentität wird nur gebunden, nicht durch den Operator ersetzt.
    [Test]
    public async Task OperatorProof_ShouldBindPersonalActorBothClientsAndCurrentInstanceWithoutMutation()
    {
        var c = new Context(); var actor = OperatorActor(); var service = OperatorService(c);
        var outcome = await service.CheckAsync(c.Instance.InstanceId, actor, CancellationToken.None);
        outcome.Status.Should().Be(TicketActionOperatorAccessStatus.Ok); var proof = outcome.Access!;
        proof.ProcessInstanceId.Should().Be(c.Instance.InstanceId); proof.MetaDefinitionId.Should().Be("definition");
        proof.DefinitionId.Should().Be(c.Instance.DefinitionId); proof.InitiatorIssuer.Should().Be(Issuer);
        proof.InitiatorSubject.Should().Be("m1"); proof.ActorIssuer.Should().Be(Issuer);
        proof.ActorSubject.Should().Be("operator-person"); proof.ActorAuthorizedClientId.Should().Be("tt-exchange");
        proof.CheckedAtUtc.Should().Be(c.Time.GetUtcNow());
        c.Reader.Requests.Should().Equal((actor.Identity!, "flowzer-api", "access"), (actor.Identity!, "flowzer-api", "operator"));
        c.Reader.HostClients.Should().Equal("tt-api"); c.Provider.OpenContexts.Should().Be(0); c.Provider.Commits.Should().Be(0);
        c.Job.Retries.Should().Be(3); c.Job.LockedBy.Should().Be(ServiceTaskJobService.BuildLockOwner(Worker, "worker-a"));
    }

    // Testzweck: Ein Entzug an einer der beiden persönlichen Grenzen liefert keinen Proof; kein Rollen-/Directorycache ersetzt das Nein.
    [TestCase("access")]
    [TestCase("operator")]
    public async Task OperatorProof_ShouldCloseOnEitherPersonalAccessDenial(string missing)
    {
        var c = new Context(); var service = OperatorService(c);
        c.Reader.DuringRead = () => c.Reader.Allowed = missing != "access" && c.Reader.Requests.Count == 1;
        var result = await service.CheckAsync(c.Instance.InstanceId, OperatorActor(), CancellationToken.None);
        result.Status.Should().Be(TicketActionOperatorAccessStatus.Denied); result.Access.Should().BeNull();
        c.Reader.Requests.Should().HaveCount(missing == "access" ? 1 : 2); c.Provider.Commits.Should().Be(0);
    }

    // Testzweck: Installation ohne explizite Rollen/TT-Audience bleibt schon vor Provider-I/O geschlossen, auch mit LegacyPermissiveRoles.
    [TestCase("auth-none")]
    [TestCase("access-role")]
    [TestCase("operator-role")]
    [TestCase("host-client")]
    [TestCase("aliased-client")]
    public async Task OperatorProof_ShouldRejectUnboundInstallationBeforeIo(string change)
    {
        var c = new Context(); var service = OperatorService(c); c.Authentication.JwtBearer.LegacyPermissiveRoles = true;
        if (change == "auth-none") c.Authentication.Scheme = "None";
        if (change == "access-role") c.Authentication.JwtBearer.RequiredRole = "";
        if (change == "operator-role") c.Authentication.JwtBearer.Roles.Operator = "";
        if (change == "host-client") c.TicketActions.ApiClientId = "";
        if (change == "aliased-client") c.TicketActions.ApiClientId = "flowzer-api";
        var result = await service.CheckAsync(c.Instance.InstanceId, OperatorActor(), CancellationToken.None);
        result.Status.Should().Be(TicketActionOperatorAccessStatus.Unavailable); result.Access.Should().BeNull();
        c.Reader.Requests.Should().BeEmpty(); c.Provider.Openings.Should().Be(0);
    }

    // Testzweck: Technische Header-/Fallback-Kontexte oder ungebundene persönliche Identitäten sind keine Betriebsakteure.
    [TestCase("fallback")]
    [TestCase("user-id")]
    [TestCase("identity")]
    [TestCase("issuer")]
    [TestCase("subject")]
    [TestCase("azp")]
    [TestCase("azp-control")]
    public async Task OperatorProof_ShouldRejectUnboundActorBeforeIo(string change)
    {
        var c = new Context(); var service = OperatorService(c); var actor = OperatorActor();
        actor = change switch
        {
            "fallback" => actor with { IsFallback = true }, "user-id" => actor with { UserId = Guid.Empty },
            "identity" => actor with { Identity = null }, "issuer" => actor with { Identity = new("https://other.invalid", "operator-person") },
            "subject" => actor with { Identity = new(Issuer, "") }, "azp" => actor with { AuthorizedClientId = null },
            _ => actor with { AuthorizedClientId = "tt\nexchange" },
        };
        var result = await service.CheckAsync(c.Instance.InstanceId, actor, CancellationToken.None);
        result.Status.Should().Be(TicketActionOperatorAccessStatus.InvalidContext); result.Access.Should().BeNull();
        c.Reader.Requests.Should().BeEmpty(); c.Provider.Openings.Should().Be(0);
    }

    // Testzweck: Gelöschte/beendete Instanzen und mehrdeutige/technische Masteridentitäten liefern kein Betriebsprofil.
    [TestCase("missing-instance", TicketActionOperatorAccessStatus.NotFound)]
    [TestCase("wrong-instance", TicketActionOperatorAccessStatus.InvalidContext)]
    [TestCase("finished", TicketActionOperatorAccessStatus.InvalidContext)]
    [TestCase("terminating", TicketActionOperatorAccessStatus.InvalidContext)]
    [TestCase("missing-initiator", TicketActionOperatorAccessStatus.InvalidContext)]
    [TestCase("duplicate-master", TicketActionOperatorAccessStatus.InvalidContext)]
    [TestCase("wrong-issuer", TicketActionOperatorAccessStatus.InvalidContext)]
    public async Task OperatorProof_ShouldRejectInvalidInstanceBeforeProviderIo(string change, TicketActionOperatorAccessStatus status)
    {
        var c = new Context(); var service = OperatorService(c); var requested = c.Instance.InstanceId; c.Change(change);
        var result = await service.CheckAsync(requested, OperatorActor(), CancellationToken.None);
        result.Status.Should().Be(status); result.Access.Should().BeNull(); c.Reader.Requests.Should().BeEmpty();
        c.Provider.OpenContexts.Should().Be(0); c.Provider.Commits.Should().Be(0);
    }

    // Testzweck: Abbruch/Migration/Umbindung während Provider-I/O entwertet Ja; dabei existiert keine offene Engine-Transaktion.
    [TestCase("finished")]
    [TestCase("terminating")]
    [TestCase("changed-job-version")]
    [TestCase("wrong-definition")]
    [TestCase("changed-subject")]
    [TestCase("duplicate-master")]
    public async Task OperatorProof_ShouldRevalidateImmutableInstanceAfterIo(string change)
    {
        var c = new Context(); var service = OperatorService(c);
        c.Reader.DuringRead = () => { c.Provider.OpenContexts.Should().Be(0); if (c.Reader.Requests.Count == 1) c.Change(change); };
        var result = await service.CheckAsync(c.Instance.InstanceId, OperatorActor(), CancellationToken.None);
        result.Status.Should().Be(TicketActionOperatorAccessStatus.InvalidContext); result.Access.Should().BeNull();
        c.Provider.Commits.Should().Be(0); c.Provider.OpenContexts.Should().Be(0);
    }

    // Testzweck: Beide Providerreads teilen ein äußeres Zehnsekundenbudget; Operatorread darf keinen zweiten unabhängigen Zeitraum öffnen.
    [Test]
    public async Task OperatorProof_ShouldBoundBothProviderReadsTogether()
    {
        var c = new Context(); var service = OperatorService(c);
        c.Reader.DuringRead = () => c.Time.Advance(TimeSpan.FromSeconds(c.Reader.Requests.Count == 1 ? 9 : 2));
        var result = await service.CheckAsync(c.Instance.InstanceId, OperatorActor(), CancellationToken.None);
        result.Status.Should().Be(TicketActionOperatorAccessStatus.Unavailable); result.Access.Should().BeNull();
        c.Reader.Requests.Should().HaveCount(2); c.Provider.Commits.Should().Be(0);
    }

    // Testzweck: Callerabbruch bleibt Callerabbruch, Quellfehler bleibt Unklarheit und keine Bestätigung eines persönlichen Entzugs.
    [TestCase(false)]
    [TestCase(true)]
    public async Task OperatorProof_ShouldDistinguishCallerCancellationAndSourceFailure(bool cancelled)
    {
        var c = new Context(); var service = OperatorService(c); using var stop = new CancellationTokenSource();
        c.Reader.DuringRead = cancelled ? () => stop.Cancel() : null;
        c.Reader.Error = cancelled ? null : new KeycloakAdminClientException(KeycloakAdminClientFailureKind.Transient, "synthetic source error");
        if (cancelled) await FluentActions.Awaiting(() => service.CheckAsync(c.Instance.InstanceId, OperatorActor(), stop.Token)).Should().ThrowAsync<OperationCanceledException>();
        else { var result = await service.CheckAsync(c.Instance.InstanceId, OperatorActor(), stop.Token); result.Status.Should().Be(TicketActionOperatorAccessStatus.Unavailable); result.Access.Should().BeNull(); }
        c.Provider.Commits.Should().Be(0); c.Provider.OpenContexts.Should().Be(0);
    }
}
