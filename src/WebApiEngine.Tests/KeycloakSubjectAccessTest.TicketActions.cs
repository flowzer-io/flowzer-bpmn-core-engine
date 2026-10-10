using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

public sealed partial class KeycloakSubjectAccessTest
{
    // Testzweck: Der reale HTTP-Leser verlangt aktuelle Rollen beider Clients;
    // Profil, Teilbaum und Gruppen werden im gemeinsamen Lauf nur einmal gelesen.
    [TestCase("root", "root", true)]
    [TestCase("root", "no-role", false)]
    [TestCase("no-role", "root", false)]
    [TestCase("no-role", "no-role", false)]
    [TestCase("root", "disabled-client", false)]
    [TestCase("disabled-client", "root", false)]
    [TestCase("child", "root", true)]
    public async Task TicketAccess_ShouldRequireBothCurrentClientRoles(string flowzer, string host, bool allowed)
    {
        using var handler = new Handler(flowzer) { HostVariant = host }; using var http = new HttpClient(handler);
        (await Client(http).HasCurrentTicketActionAccessAsync(Identity, ClientId, "access", HostClientId, CancellationToken.None)).Should().Be(allowed);
        handler.TokenRequests.Should().Be(1);
        handler.Requests.Count(p => p.EndsWith("/users/user-a", StringComparison.Ordinal)).Should().Be(1);
        handler.Requests.Count(p => p.EndsWith("/groups/demo-root", StringComparison.Ordinal)).Should().Be(1);
        if (allowed)
        {
            handler.Requests.Should().Contain($"/admin/realms/flowzer/users/user-a/role-mappings/clients/{HostClientUuid}/composite");
            handler.Requests.Should().NotContain(p => p.Contains($"/clients/{HostClientId}/composite", StringComparison.Ordinal));
        }
    }

    // Testzweck: Technische Fehler oder mehrdeutige/fremde Hostrollen sind kein
    // persönlicher Rechteentzug, keine Zustimmung und kein Detail-/Secret-Leak.
    [TestCase("unavailable", KeycloakAdminClientFailureKind.Transient)]
    [TestCase("forbidden", KeycloakAdminClientFailureKind.Authorization)]
    [TestCase("missing-client", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("wrong-client", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("duplicate-client", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("aliased-client", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("missing-roles", KeycloakAdminClientFailureKind.NotFound)]
    [TestCase("realm-role", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("foreign-role", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("duplicate-role", KeycloakAdminClientFailureKind.InvalidResponse)]
    public async Task TicketAccess_ShouldKeepHostUncertaintyDistinctFromDenial(string variant, KeycloakAdminClientFailureKind expected)
    {
        using var handler = new Handler("root") { HostVariant = variant }; using var http = new HttpClient(handler);
        var error = await FluentActions.Awaiting(() => Client(http).HasCurrentTicketActionAccessAsync(Identity,
            ClientId, "access", HostClientId, CancellationToken.None)).Should().ThrowAsync<KeycloakAdminClientException>();
        error.Which.Kind.Should().Be(expected); error.Which.InnerException.Should().BeNull();
        error.Which.ToString().Should().NotContain("token-private").And.NotContain("synthetic-only").And.NotContain("PRIVATE_HTTP_INPUT");
    }

    // Testzweck: Clientparameter bleiben unverändert und verschieden; der Host darf
    // weder leer noch derselbe Client sein. Ungültige Bindungen starten keinen HTTP-Aufruf.
    [TestCase("")]
    [TestCase("flowzer-api")]
    [TestCase("tt/api")]
    [TestCase("tt-api ")]
    [TestCase("tt%2Fapi")]
    public async Task TicketAccess_ShouldRejectUnsafeOrAliasedHostBeforeHttp(string host)
    {
        using var handler = new Handler("root"); using var http = new HttpClient(handler);
        var error = await FluentActions.Awaiting(() => Client(http).HasCurrentTicketActionAccessAsync(Identity,
            ClientId, "access", host, CancellationToken.None)).Should().ThrowAsync<KeycloakAdminClientException>();
        error.Which.Kind.Should().Be(KeycloakAdminClientFailureKind.Configuration); handler.Requests.Should().BeEmpty();
    }

    // Testzweck: Auch TT-only-Rollenentzug wird im nächsten Aufruf frisch bemerkt;
    // Konto, Scope und Flowzer bleiben dabei aktiv. Kein positiver Stand wird gecacht.
    [Test]
    public async Task TicketAccess_ShouldObserveHostOnlyRevocationOnNextProbe()
    {
        using var handler = new Handler("root"); using var http = new HttpClient(handler); var client = Client(http);
        (await client.HasCurrentTicketActionAccessAsync(Identity, ClientId, "access", HostClientId, CancellationToken.None)).Should().BeTrue();
        handler.HostVariant = "no-role";
        (await client.HasCurrentTicketActionAccessAsync(Identity, ClientId, "access", HostClientId, CancellationToken.None)).Should().BeFalse();
        handler.TokenRequests.Should().Be(2);
    }

    // Testzweck: Beide Clients teilen zehn Sekunden einschließlich TT-HTTP-I/O.
    // Nach sechs Sekunden Flowzer bleibt kein zweites Zehn-Sekunden-Budget; Caller-Abbruch bleibt Abbruch.
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task TicketAccess_ShouldShareOneDeadlineAndPreserveCallerCancellation(bool callerCancels, bool bodyHangs)
    {
        var time = new FakeTimeProvider(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler("root") { HangingHostRole = bodyHangs ? null : entered,
            HangingHostRoleBody = bodyHangs ? entered : null,
            DuringRequest = p => { if (p.EndsWith($"/clients/{ClientUuid}/composite", StringComparison.Ordinal)) time.Advance(TimeSpan.FromSeconds(6)); } };
        using var http = new HttpClient(handler); using var cancellation = new CancellationTokenSource();
        var action = Client(http, time: time).HasCurrentTicketActionAccessAsync(Identity, ClientId, "access", HostClientId, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (callerCancels) cancellation.Cancel(); else time.Advance(TimeSpan.FromSeconds(5));
        if (callerCancels)
            (await FluentActions.Awaiting(() => action).Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        else
            (await FluentActions.Awaiting(() => action).Should().ThrowAsync<KeycloakAdminClientException>()).Which.Kind.Should().Be(KeycloakAdminClientFailureKind.Transient);
    }

    // Testzweck: Nach sechs verbrauchten Sekunden gehört auch der lange Retry-Backoff
    // des zweiten Clients zum EINEN Gesamtbudget. 503, Transport- und Body-I/O-Fehler
    // dürfen weder die Deadline noch Caller-Abbruch abkoppeln oder einen zweiten Request senden.
    [TestCase("status", false)]
    [TestCase("request", false)]
    [TestCase("body", false)]
    [TestCase("status", true)]
    [TestCase("request", true)]
    [TestCase("body", true)]
    public async Task TicketAccess_ShouldBoundHostRetryBackoffAfterSpentBudget(string failure, bool callerCancels)
    {
        var time = new FakeTimeProvider(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = Options(); options.MaxRetries = 2; options.RetryDelayMilliseconds = 30_000;
        using var handler = new Handler("root") { HostRetryFailure = failure, HostRetryEntered = entered,
            DuringRequest = p => { if (p.EndsWith($"/clients/{ClientUuid}/composite", StringComparison.Ordinal)) time.Advance(TimeSpan.FromSeconds(6)); } };
        using var http = new HttpClient(handler); using var cancellation = new CancellationTokenSource();
        var action = Client(http, options, time).HasCurrentTicketActionAccessAsync(Identity, ClientId, "access", HostClientId, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (callerCancels) cancellation.Cancel(); else time.Advance(TimeSpan.FromSeconds(5));
        // Eine abgekoppelte 30s-Wartezeit wird als Testfehler sichtbar, nicht als
        // unendlich hängender Lauf. Der normale Produktpfad endet durch linked CT.
        if (callerCancels)
            (await FluentActions.Awaiting(() => action.WaitAsync(TimeSpan.FromSeconds(5)))
                .Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        else
            (await FluentActions.Awaiting(() => action.WaitAsync(TimeSpan.FromSeconds(5)))
                .Should().ThrowAsync<KeycloakAdminClientException>()).Which.Kind.Should().Be(KeycloakAdminClientFailureKind.Transient);
        handler.Requests.Count(p => p.EndsWith($"/clients/{HostClientUuid}/composite", StringComparison.Ordinal)).Should().Be(1);
        handler.TokenRequests.Should().Be(1);
    }
}
