using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Time.Testing;
using WebApiEngine.Auth;

namespace WebApiEngine.Tests;

public sealed class BffSessionStoreTest
{
    // Testzweck: Parallele API-Abfragen erneuern ein ablaufendes Token genau einmal;
    // Rollen werden dabei ersetzt, ohne den Nutzer aus seiner Arbeit zu werfen.
    [Test]
    public async Task ConcurrentRetrieval_ShouldRefreshOnceAndReplaceClaims()
    {
        var clock = new FakeTimeProvider();
        var refresher = new FakeRefresher(clock);
        using var store = new BffSessionStore(refresher, clock);
        var key = await store.StoreAsync(Ticket(clock));
        var tickets = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.RetrieveAsync(key)));
        refresher.Calls.Should().Be(1);
        tickets.Should().OnlyContain(ticket => ticket != null && ticket.Principal.FindFirst("roles")!.Value == "access");
    }

    // Testzweck: Abgemeldete und absolut abgelaufene Sitzungen dürfen weder durch
    // Cookie-Erneuerung noch durch einen Refresh wiederbelebt werden.
    [Test]
    public async Task RevokedAndExpiredSessions_ShouldStayInvalid()
    {
        var clock = new FakeTimeProvider();
        using var store = new BffSessionStore(new FakeRefresher(clock), clock);
        var ticket = Ticket(clock);
        var key = await store.StoreAsync(ticket);
        await store.RemoveAsync(key);
        await store.RenewAsync(key, ticket);
        (await store.RetrieveAsync(key)).Should().BeNull();
        key = await store.StoreAsync(ticket);
        clock.Advance(TimeSpan.FromHours(9));
        (await store.RetrieveAsync(key)).Should().BeNull();
    }

    // Testzweck: Ein vorübergehender Provider-Ausfall ist kein Logout; ein späterer
    // Wiederholungsversuch darf dieselbe noch vorhandene Sitzung erfolgreich erneuern.
    [Test]
    public async Task TransientFailure_ShouldRetainSessionForRetry()
    {
        var clock = new FakeTimeProvider();
        var refresher = new FakeRefresher(clock) { Unavailable = true };
        using var store = new BffSessionStore(refresher, clock);
        var key = await store.StoreAsync(Ticket(clock));
        await FluentActions.Awaiting(() => store.RetrieveAsync(key)).Should().ThrowAsync<BffSessionUnavailableException>();
        refresher.Unavailable = false;
        (await store.RetrieveAsync(key)).Should().NotBeNull();
    }

    // Testzweck: Eine explizite erneute Anmeldung darf nicht weiter die alte Person
    // anzeigen, nur weil der Cookiehandler dafür ITicketStore.RenewAsync verwendet.
    [Test]
    public async Task ExplicitSignIn_ShouldReplaceExistingIdentity()
    {
        var clock = new FakeTimeProvider();
        using var store = new BffSessionStore(new FakeRefresher(clock), clock);
        var key = await store.StoreAsync(Ticket(clock));
        var properties = new AuthenticationProperties { ExpiresUtc = clock.GetUtcNow().AddHours(8) };
        var next = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "new-person")], "test")), properties, "test");
        await store.RenewAsync(key, next);
        (await store.RetrieveAsync(key))!.Principal.FindFirstValue("sub").Should().Be("new-person");
    }

    private static AuthenticationTicket Ticket(TimeProvider clock)
    {
        var properties = new AuthenticationProperties { ExpiresUtc = clock.GetUtcNow().AddHours(8) };
        properties.Items[BffSessionStore.AccessTokenExpiry] = clock.GetUtcNow().ToString("O");
        return new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "test"), new Claim("roles", "operator")], "test")), properties, "test");
    }

    private sealed class FakeRefresher(TimeProvider clock) : IBffSessionRefresher
    {
        public int Calls { get; private set; }
        public bool Unavailable { get; set; }
        public async Task<AuthenticationTicket?> RefreshAsync(AuthenticationTicket ticket)
        {
            Calls++;
            await Task.Yield();
            if (Unavailable) throw new BffSessionUnavailableException();
            ticket.Properties.Items[BffSessionStore.AccessTokenExpiry] = clock.GetUtcNow().AddMinutes(5).ToString("O");
            return new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "test"), new Claim("roles", "access")], "test")), ticket.Properties, "test");
        }
    }
}
