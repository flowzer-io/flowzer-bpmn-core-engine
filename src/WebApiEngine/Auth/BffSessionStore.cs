using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace WebApiEngine.Auth;

/// <summary>
/// Das Cookie enthält nur einen zufälligen Sitzungsschlüssel. Refresh-Tokens bleiben
/// ausschließlich im API-Prozess; parallele Requests teilen eine einzige Erneuerung.
/// Ein API-Neustart verlangt bewusst eine neue SSO-Anmeldung (kein Token auf Datenträger).
/// </summary>
public sealed class BffSessionStore(IBffSessionRefresher refresher, TimeProvider clock) : ITicketStore, IDisposable
{
    internal const string AccessTokenExpiry = ".flowzer.access-token-expires";
    private readonly MemoryCache _sessions = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions.Set(key, new Session(Copy(ticket)), new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8)
        });
        return Task.FromResult(key);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!_sessions.TryGetValue<Session>(key, out var session) || session is null) return null;
        await session.Gate.WaitAsync();
        try
        {
            if (session.Revoked || session.Ticket.Properties.ExpiresUtc <= clock.GetUtcNow())
            {
                session.Revoked = true;
                _sessions.Remove(key);
                return null;
            }
            if (DateTimeOffset.TryParse(session.Ticket.Properties.Items.TryGetValue(AccessTokenExpiry, out var expiryValue) ? expiryValue : null,
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry)
                && expiry <= clock.GetUtcNow().AddSeconds(60))
            {
                // Der Refresher arbeitet auf einer Kopie: ein Teilfehler darf den
                // noch gültigen Stand und insbesondere das Rotationstoken nicht verändern.
                var renewed = await refresher.RefreshAsync(Copy(session.Ticket));
                if (renewed is null)
                {
                    session.Revoked = true;
                    _sessions.Remove(key);
                    return null;
                }
                session.Ticket = renewed;
            }
            return Copy(session.Ticket);
        }
        finally { session.Gate.Release(); }
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (!_sessions.TryGetValue<Session>(key, out var session) || session is null) return;
        await session.Gate.WaitAsync();
        try
        {
            // SlidingExpiration und AllowRefresh sind abgeschaltet. RenewAsync wird
            // deshalb nur für einen expliziten erneuten Sign-in verwendet; ein Kontowechsel
            // muss dabei den alten serverseitigen Stand vollständig ersetzen.
            if (!session.Revoked) session.Ticket = Copy(ticket);
        }
        finally { session.Gate.Release(); }
    }

    public async Task RemoveAsync(string key)
    {
        if (!_sessions.TryGetValue<Session>(key, out var session) || session is null) return;
        await session.Gate.WaitAsync();
        try { session.Revoked = true; _sessions.Remove(key); }
        finally { session.Gate.Release(); }
    }

    public void Dispose() => _sessions.Dispose();
    private static AuthenticationTicket Copy(AuthenticationTicket ticket) =>
        TicketSerializer.Default.Deserialize(TicketSerializer.Default.Serialize(ticket))!;
    private sealed class Session(AuthenticationTicket ticket)
    {
        internal AuthenticationTicket Ticket { get; set; } = ticket;
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal bool Revoked { get; set; }
    }
}

/// <summary>Providergrenze für Tokenrotation und erneute Prüfung der wirksamen Rechte.</summary>
public interface IBffSessionRefresher
{
    Task<AuthenticationTicket?> RefreshAsync(AuthenticationTicket ticket);
}

/// <summary>Provider nicht erreichbar: 503 statt irreführendem Logout/401.</summary>
public sealed class BffSessionUnavailableException() : Exception("Die Anmeldung kann gerade nicht erneuert werden. Bitte erneut versuchen.");
