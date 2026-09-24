using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WebApiEngine.Auth;

/// <summary>Erneuert serverseitig am konfigurierten OIDC-Provider, niemals an einem Browserziel.</summary>
public sealed class BffSessionRefresher(
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    FlowzerAuthenticationOptions options,
    BffAccessTokenClaimsValidator validator,
    TimeProvider clock) : IBffSessionRefresher
{
    public async Task<AuthenticationTicket?> RefreshAsync(AuthenticationTicket ticket)
    {
        var refreshToken = ticket.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        var oidc = oidcOptions.Get(FlowzerAuthenticationSchemes.OpenIdConnect);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            if (oidc.ConfigurationManager is null) throw new BffSessionUnavailableException();
            var metadata = await oidc.ConfigurationManager.GetConfigurationAsync(timeout.Token);
            if (!Uri.TryCreate(metadata.TokenEndpoint, UriKind.Absolute, out var endpoint)
                || (oidc.RequireHttpsMetadata && endpoint.Scheme != Uri.UriSchemeHttps))
                throw new BffSessionUnavailableException();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken,
                ["client_id"] = options.Bff.ClientId, ["client_secret"] = options.Bff.ClientSecret
            });
            using var response = await oidc.Backchannel.PostAsync(endpoint, content, timeout.Token);
            if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
                throw new BffSessionUnavailableException();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (!response.IsSuccessStatusCode)
            {
                // Nur ein definitiv widerrufener Grant beendet die Session. Infrastruktur-
                // und Clientkonfigurationsfehler dürfen nicht wie ein Logout erscheinen.
                if (body.RootElement.TryGetProperty("error", out var error) && error.GetString() == "invalid_grant") return null;
                throw new BffSessionUnavailableException();
            }
            var accessToken = body.RootElement.GetProperty("access_token").GetString()!;
            var principal = await validator.ValidateAccessTokenAsync(accessToken, ticket.Principal, metadata, timeout.Token);
            var expires = new DateTimeOffset(new JsonWebToken(accessToken).ValidTo);
            if (expires <= clock.GetUtcNow()) return null;
            ticket.Properties.Items[BffSessionStore.AccessTokenExpiry] = expires.ToString("O", CultureInfo.InvariantCulture);
            var nextRefresh = body.RootElement.TryGetProperty("refresh_token", out var rotation)
                ? rotation.GetString() : refreshToken;
            if (string.IsNullOrWhiteSpace(nextRefresh)) return null;
            var serverSideTokens = new List<AuthenticationToken> { new() { Name = "refresh_token", Value = nextRefresh } };
            if (options.Bff.ProviderLogout)
            {
                // StoreTokens ersetzt alle Tokens des Tickets. Ein neues ID-Token aus der
                // Antwort gilt nur dann als aktueller id_token_hint, wenn es dieselbe Anmeldung
                // beschreibt; sonst bleibt das bisherige erhalten.
                var previousIdToken = ticket.Properties.GetTokenValue(BffSessionStore.IdToken);
                var renewedIdToken = body.RootElement.TryGetProperty("id_token", out var renewed)
                                     && renewed.ValueKind == JsonValueKind.String
                    ? renewed.GetString()
                    : null;
                var idToken = IsSameIdentity(previousIdToken, renewedIdToken) ? renewedIdToken : previousIdToken;
                if (!string.IsNullOrWhiteSpace(idToken))
                    serverSideTokens.Add(new AuthenticationToken { Name = BffSessionStore.IdToken, Value = idToken });
            }
            ticket.Properties.StoreTokens(serverSideTokens);
            return new AuthenticationTicket(principal, ticket.Properties, ticket.AuthenticationScheme);
        }
        catch (SecurityTokenException) { return null; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            // Keine Providerantwort oder Tokenwerte als Exception-Detail/InnerException loggen.
            throw new BffSessionUnavailableException();
        }
    }

    /// <summary>
    /// Vergleicht iss, aud und sub zweier ID-Tokens ohne Signaturpruefung: Das neue Token kam
    /// ueber den vertraulichen Token-Endpunkt und dient nur als id_token_hint. Ohne bisheriges
    /// Token gibt es keinen Vergleich und damit keine Uebernahme. Ein unlesbares Token gilt als
    /// abweichend und darf die Erneuerung selbst nicht scheitern lassen.
    /// </summary>
    private static bool IsSameIdentity(string? previousIdToken, string? renewedIdToken)
    {
        if (string.IsNullOrWhiteSpace(previousIdToken) || string.IsNullOrWhiteSpace(renewedIdToken)) return false;
        try
        {
            var previous = new JsonWebToken(previousIdToken);
            var renewed = new JsonWebToken(renewedIdToken);
            return !string.IsNullOrEmpty(renewed.Subject)
                   && string.Equals(previous.Issuer, renewed.Issuer, StringComparison.Ordinal)
                   && string.Equals(previous.Subject, renewed.Subject, StringComparison.Ordinal)
                   && previous.Audiences.ToHashSet(StringComparer.Ordinal).SetEquals(renewed.Audiences);
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException or JsonException)
        {
            return false;
        }
    }
}
