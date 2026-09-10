using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.Shared;
using WebApiEngine.Ai;

namespace WebApiEngine.Tests;

/// <summary>
/// Prueft die konfigurierbare Authentifizierung (JWT Bearer / OIDC) und die CORS-Richtlinie der Web-API.
/// Die Tests ersetzen nur die OIDC-Metadatenabfrage durch einen statischen Signaturschluessel;
/// Handler, Policies und Claim-Aufloesung laufen wie in Produktion.
/// </summary>
[NonParallelizable]
public class AuthenticationAndCorsIntegrationTest
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-integration-test-signing-key-with-32-bytes+"));

    // Testzweck: Mit aktiviertem JWT-Bearer-Schema sind Fachendpunkte ohne Token nicht erreichbar (401),
    // auch solche, die bisher keinen Benutzerkontext verlangten (Definitionskatalog).
    [Test]
    public async Task ProtectedEndpoints_ShouldReturnUnauthorized_WhenJwtBearerIsEnabledAndNoTokenIsSent()
    {
        await using var factory = CreateJwtFactory(new TestStorage());
        using var client = factory.CreateClient();

        var definitions = await client.GetAsync("/definition/meta");
        var userTasks = await client.GetAsync("/usertask");
        var diagnostics = await client.GetAsync("/operations/diagnostics");

        definitions.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        userTasks.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        diagnostics.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Health-Endpunkte muessen fuer Orchestrator-Probes ohne Token erreichbar bleiben.
    [Test]
    public async Task HealthEndpoints_ShouldStayAnonymous_WhenJwtBearerIsEnabled()
    {
        await using var factory = CreateJwtFactory(new TestStorage());
        using var client = factory.CreateClient();

        var liveness = await client.GetAsync("/health");
        var readiness = await client.GetAsync("/health/ready");

        liveness.StatusCode.Should().Be(HttpStatusCode.OK);
        readiness.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein gueltiges Token des konfigurierten Issuers oeffnet die Fachendpunkte und liefert
    // die Benutzer-Id aus dem `sub`-Claim an die User-Task-Abfrage.
    [Test]
    public async Task UserTasks_ShouldResolveUserFromSubjectClaim_WhenValidTokenIsSent()
    {
        var storage = new TestStorage();
        var userId = Guid.NewGuid();
        await using var factory = CreateJwtFactory(storage);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId));

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>();
        payload!.Successful.Should().BeTrue();
        storage.LastRequestedUserTaskUserId.Should().Be(userId);
    }

    // Testzweck: Entra ID liefert die Benutzer-Id im Claim `oid`, nicht in `sub`. Der Claim muss
    // unter seinem Originalnamen ankommen (kein Inbound-Claim-Mapping) und als Benutzer-Id gelten.
    [Test]
    public async Task UserTasks_ShouldResolveUserFromOidClaim_WhenSubjectIsNotAGuid()
    {
        var storage = new TestStorage();
        var userId = Guid.NewGuid();
        await using var factory = CreateJwtFactory(storage);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(claims: [new Claim("sub", "opaque-subject-from-entra"), new Claim("oid", userId.ToString())]));

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.LastRequestedUserTaskUserId.Should().Be(userId);
    }

    // Testzweck: Ein Token eines fremden Issuers oder mit falscher Audience wird abgelehnt.
    [Test]
    public async Task ProtectedEndpoints_ShouldReturnUnauthorized_WhenTokenIssuerOrAudienceDoesNotMatch()
    {
        await using var factory = CreateJwtFactory(new TestStorage());
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(Guid.NewGuid(), issuer: "https://someone-else.test"));
        var wrongIssuer = await client.GetAsync("/definition/meta");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(Guid.NewGuid(), audience: "other-api"));
        var wrongAudience = await client.GetAsync("/definition/meta");

        wrongIssuer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongAudience.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Der technische Development-Header darf bei aktivem JWT-Bearer keinen Zugang mehr
    // verschaffen, auch nicht im Development-Environment.
    [Test]
    public async Task DevelopmentUserHeader_ShouldNotBypassAuthentication_WhenJwtBearerIsEnabled()
    {
        await using var factory = CreateJwtFactory(new TestStorage(), environmentName: "Development");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Flowzer-UserId", Guid.NewGuid().ToString());

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Eine JWT-Bearer-Konfiguration ohne Authority oder Audience ist ein Betriebsfehler und
    // muss den Host-Start abbrechen statt still ohne Schutz zu laufen.
    [Test]
    public void Host_ShouldFailToStart_WhenJwtBearerIsEnabledWithoutAuthority()
    {
        var factory = new TestWebApplicationFactory(new TestStorage(), "Production", new Dictionary<string, string?>
        {
            ["Authentication:Scheme"] = "JwtBearer",
            ["Authentication:JwtBearer:Audience"] = Audience
        });

        var action = () => factory.CreateClient();

        action.Should().Throw<Exception>().Where(exception =>
            exception.ToString().Contains("Authentication:JwtBearer:Authority", StringComparison.Ordinal));
    }

    // Testzweck: Ohne CORS-Konfiguration sendet die API ausserhalb von Development keine
    // Access-Control-Allow-Origin-Header mehr; die bisherige Wildcard galt fuer jede Umgebung.
    [Test]
    public async Task Cors_ShouldNotAllowAnyOrigin_WhenNothingIsConfiguredOutsideDevelopment()
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage(), "Production");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://console.example");

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    // Testzweck: Konfigurierte Origins werden exakt zugelassen, alle anderen nicht.
    [Test]
    public async Task Cors_ShouldAllowOnlyConfiguredOrigins()
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage(), "Production", new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://console.example"
        });
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("Origin", "https://console.example");
        var allowed = await client.GetAsync("/health");
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        var denied = await client.GetAsync("/health");

        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://console.example");
        denied.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    // Testzweck: Im Development-Environment bleibt der bisherige Komfort erhalten: ohne Konfiguration
    // wird jede Origin zugelassen, damit Blazor-Dev-Server und Playwright weiter funktionieren.
    [Test]
    public async Task Cors_ShouldAllowAnyOrigin_WhenNothingIsConfiguredInDevelopment()
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage(), "Development");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://console.example");

        var response = await client.GetAsync("/health");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("*");
    }

    // Testzweck: Mit konfigurierter Pflichtrolle reicht ein gueltiges Token nicht; ohne die Rolle
    // antwortet die API 403, damit selbstregistrierte Realm-Konten keinen Zugang bekommen.
    [Test]
    public async Task ProtectedEndpoints_ShouldReturnForbidden_WhenRequiredRoleIsMissing()
    {
        await using var factory = CreateJwtFactory(new TestStorage(), requiredRole: "access");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(Guid.NewGuid()));

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Testzweck: Keycloak liefert Clientrollen unter `resource_access.<audience>.roles` als JSON-Objekt
    // im Token; genau diese Struktur muss die Pflichtrolle erfuellen, Rollen anderer Clients nicht.
    [Test]
    public async Task ProtectedEndpoints_ShouldHonorKeycloakClientRoles_WhenRequiredRoleIsConfigured()
    {
        var storage = new TestStorage();
        var userId = Guid.NewGuid();
        await using var factory = CreateJwtFactory(storage, requiredRole: "access");
        using var client = factory.CreateClient();

        var keycloakToken = CreateToken(claims:
        [
            new Claim("sub", userId.ToString()),
            new Claim("resource_access", """{"flowzer-api":{"roles":["access"]}}""", JsonClaimValueTypes.Json)
        ]);
        // Der Testtoken traegt den Claim wie Keycloak als JSON-Objekt, nicht als String.
        new JsonWebToken(keycloakToken).TryGetPayloadValue<System.Text.Json.JsonElement>("resource_access", out var resourceAccess).Should().BeTrue();
        resourceAccess.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", keycloakToken);
        var allowed = await client.GetAsync("/usertask");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(claims:
        [
            new Claim("sub", userId.ToString()),
            new Claim("resource_access", """{"other-api":{"roles":["access"]}}""", JsonClaimValueTypes.Json)
        ]));
        var otherClient = await client.GetAsync("/usertask");

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.LastRequestedUserTaskUserId.Should().Be(userId);
        otherClient.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Testzweck: Entra ID liefert App-Rollen im Claim `roles`; die Pflichtrolle gilt auch dort.
    [Test]
    public async Task ProtectedEndpoints_ShouldHonorEntraAppRoles_WhenRequiredRoleIsConfigured()
    {
        await using var factory = CreateJwtFactory(new TestStorage(), requiredRole: "access");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(claims:
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("roles", "reader"),
            new Claim("roles", "access")
        ]));

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Der BFF-Modus muss unvollstaendige vertrauliche OIDC- und
    // Data-Protection-Konfigurationen beim Hoststart ablehnen statt ungeschuetzt zu starten.
    [Test]
    public void Host_ShouldFailToStart_WhenBffConfigurationIsIncomplete()
    {
        using var factory = CreateBffFactory(new TestStorage(), bffClientSecret: null);

        var action = () => factory.CreateClient();

        action.Should().Throw<Exception>().Where(exception =>
            exception.ToString().Contains("Authentication:Bff:ClientSecret", StringComparison.Ordinal));
    }

    // Testzweck: Eine BFF-Cookie-Session soll nur die minimale Benutzerprojektion ausgeben und
    // denselben GUID-Benutzerkontext wie der bestehende Bearer-Pfad verwenden.
    [Test]
    public async Task BffSession_ShouldExposeMinimalProfile_AndPreserveUserContext()
    {
        var storage = new TestStorage();
        await using var factory = CreateBffFactory(storage);
        using var client = CreateSecureCookieClient(factory);
        var userId = Guid.NewGuid();

        var signIn = await client.GetAsync($"/__tests/bff/signin?userId={userId}");
        var session = await client.GetAsync("/bff/session");
        var tasks = await client.GetAsync("/usertask");
        var json = await session.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        signIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        signIn.Headers.GetValues("Set-Cookie").Should().ContainSingle(value =>
            value.StartsWith("__Host-Flowzer-Session=", StringComparison.Ordinal)
            && value.Contains("path=/", StringComparison.OrdinalIgnoreCase)
            && value.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && value.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && value.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        session.Headers.CacheControl!.NoStore.Should().BeTrue();
        json.GetProperty("id").GetString().Should().Be(userId.ToString());
        json.GetProperty("name").GetString().Should().Be("Ada Lovelace");
        json.GetProperty("email").GetString().Should().Be("ada@example.test");
        json.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString())
            .Should().BeEquivalentTo(
                "access", "modeler", "operator", "worker", "aiConnectionUse", "aiConnectionManage");
        json.TryGetProperty("accessToken", out _).Should().BeFalse();
        json.TryGetProperty("refreshToken", out _).Should().BeFalse();
        tasks.StatusCode.Should().Be(HttpStatusCode.OK);
        storage.LastRequestedUserTaskUserId.Should().Be(userId);
    }

    // Testzweck: Cookie-authentifizierte Schreibzugriffe muessen vor der Controller-Ausfuehrung
    // sowohl einen gueltigen Origin als auch ein Antiforgery-Token nachweisen.
    [Test]
    public async Task BffCookieMutation_ShouldRequireSameOriginAndCsrfToken()
    {
        await using var factory = CreateBffFactory(new TestStorage());
        using var client = CreateSecureCookieClient(factory);
        await client.GetAsync($"/__tests/bff/signin?userId={Guid.NewGuid()}");

        var missingEverything = await client.PostAsync("/__tests/bff/mutate", null);
        using var csrfResponse = await client.GetAsync("/bff/csrf");
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        csrfResponse.Headers.GetValues("Set-Cookie").Should().ContainSingle(value =>
            value.StartsWith("__Host-Flowzer-Csrf=", StringComparison.Ordinal)
            && value.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && value.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        var headerName = csrf.GetProperty("headerName").GetString()!;
        var requestToken = csrf.GetProperty("requestToken").GetString()!;

        using var missingTokenRequest = new HttpRequestMessage(HttpMethod.Post, "/__tests/bff/mutate");
        missingTokenRequest.Headers.Add("Origin", "https://localhost");
        var missingToken = await client.SendAsync(missingTokenRequest);
        using var invalidTokenRequest = new HttpRequestMessage(HttpMethod.Post, "/__tests/bff/mutate");
        invalidTokenRequest.Headers.Add("Origin", "https://localhost");
        invalidTokenRequest.Headers.Add(headerName, "not-an-antiforgery-token");
        var invalidToken = await client.SendAsync(invalidTokenRequest);

        using var foreignOriginRequest = new HttpRequestMessage(HttpMethod.Post, "/__tests/bff/mutate");
        foreignOriginRequest.Headers.Add(headerName, requestToken);
        foreignOriginRequest.Headers.Add("Origin", "https://evil.example");
        var foreignOrigin = await client.SendAsync(foreignOriginRequest);

        using var validRequest = new HttpRequestMessage(HttpMethod.Post, "/__tests/bff/mutate");
        validRequest.Headers.Add(headerName, requestToken);
        validRequest.Headers.Add("Origin", "https://localhost");
        var valid = await client.SendAsync(validRequest);

        missingEverything.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingEverything.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        foreignOrigin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidToken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        valid.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.Services.GetRequiredService<BffMutationProbe>().Executions.Should().Be(1);
    }

    // Testzweck: Externe Bearer-Clients bleiben im BFF-Modus CSRF-frei kompatibel; ein absichtlich
    // gesendeter ungueltiger Bearer-Header darf dagegen nicht auf ein vorhandenes Cookie fallen.
    [Test]
    public async Task BffMutation_ShouldKeepBearerCompatibility_WithoutCookieFallback()
    {
        await using var factory = CreateBffFactory(new TestStorage());
        using var client = CreateSecureCookieClient(factory);
        await client.GetAsync($"/__tests/bff/signin?userId={Guid.NewGuid()}");

        using var validBearer = new HttpRequestMessage(HttpMethod.Post, "/__tests/bff/mutate");
        validBearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(claims:
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("resource_access", """{"flowzer-api":{"roles":["access"]}}""", JsonClaimValueTypes.Json)
        ]));
        var allowed = await client.SendAsync(validBearer);

        using var invalidBearer = new HttpRequestMessage(HttpMethod.Post, "/__tests/bff/mutate");
        invalidBearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        var denied = await client.SendAsync(invalidBearer);

        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Services.GetRequiredService<BffMutationProbe>().Executions.Should().Be(1);
    }

    // Testzweck: Login-Ruecksprungziele muessen serverseitig auf lokale Pfade begrenzt sein und
    // Logout darf die Cookie-Session nur mit gueltigem CSRF-Nachweis beenden.
    [Test]
    public async Task BffLoginAndLogout_ShouldRejectOpenRedirects_AndProtectSignOut()
    {
        await using var factory = CreateBffFactory(new TestStorage());
        using var client = CreateSecureCookieClient(factory);

        var invalidAbsolute = await client.GetAsync("/bff/login?returnTo=https%3A%2F%2Fevil.example");
        var invalidProtocolRelative = await client.GetAsync("/bff/login?returnTo=%2F%2Fevil.example");
        var validLocal = await client.GetAsync("/bff/login?returnTo=%2Ftasks%3Ftask%3D42");
        await client.GetAsync($"/__tests/bff/signin?userId={Guid.NewGuid()}");
        var logoutWithoutCsrf = await client.PostAsync("/bff/logout", null);

        using var csrfResponse = await client.GetAsync("/bff/csrf");
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logoutRequest.Headers.Add(csrf.GetProperty("headerName").GetString()!, csrf.GetProperty("requestToken").GetString()!);
        logoutRequest.Headers.Add("Origin", "https://localhost");
        var logout = await client.SendAsync(logoutRequest);
        var sessionAfterLogout = await client.GetAsync("/bff/session");

        invalidAbsolute.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidProtocolRelative.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        validLocal.StatusCode.Should().Be(HttpStatusCode.Redirect);
        validLocal.Headers.Location!.ToString().Should().StartWith($"{Issuer}/protocol/openid-connect/auth");
        logoutWithoutCsrf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        sessionAfterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Ein gueltig angemeldetes Konto ohne Freischaltung muss seine minimale Sitzung
    // sehen und CSRF-geschuetzt abmelden koennen, ohne dadurch Fachzugriff zu erhalten.
    [Test]
    public async Task BffSessionWithoutAccessRole_ShouldAllowSessionManagementButDenyBusinessAccess()
    {
        await using var factory = CreateBffFactory(new TestStorage());
        using var client = CreateSecureCookieClient(factory);
        await client.GetAsync($"/__tests/bff/signin?userId={Guid.NewGuid()}&hasAccess=false");

        var session = await client.GetAsync("/bff/session");
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await session.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        profile.GetProperty("capabilities").GetArrayLength().Should().Be(0);
        (await client.GetAsync("/definition/meta")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var csrfResponse = await client.GetAsync("/bff/csrf");
        csrfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logoutRequest.Headers.Add(csrf.GetProperty("headerName").GetString()!, csrf.GetProperty("requestToken").GetString()!);
        logoutRequest.Headers.Add("Origin", "https://localhost");
        (await client.SendAsync(logoutRequest)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/bff/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Ohne BFF muss der explizit anonyme Login-Endpunkt 404 statt eines DI-Fehlers
    // liefern; die optionale Antiforgery-Registrierung darf den Bearer-Betrieb nicht brechen.
    [Test]
    public async Task BffLogin_ShouldReturnNotFound_WhenOnlyBearerIsEnabled()
    {
        await using var factory = CreateJwtFactory(new TestStorage());
        using var client = factory.CreateClient();

        (await client.GetAsync("/bff/login")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Verwenden und Verwalten von KI-Verbindungen sind getrennte, fail-closed
    // Rollen; ein reiner Verwender darf lesen, aber keine Secret-Referenz schreiben.
    [Test]
    public async Task AiConnectionPolicies_ShouldSeparateUseAndManagementRoles()
    {
        var storage = new TestStorage();
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Scheme"] = "JwtBearer",
            ["Authentication:JwtBearer:Authority"] = Issuer,
            ["Authentication:JwtBearer:Audience"] = Audience,
            ["Authentication:JwtBearer:Roles:AiConnectionUser"] = "ai-user",
            ["Authentication:JwtBearer:Roles:AiConnectionManager"] = "ai-manager",
            ["Ai:AllowCloudProviders"] = "true"
        };
        await using var factory = new TestWebApplicationFactory(
            storage, "Production", settings, useStaticSigningKey: true);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken([new Claim("sub", Guid.NewGuid().ToString())]));
        (await client.GetAsync("/ai/connection")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/ai/tool")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken([
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("roles", "ai-user")
            ]));
        (await client.GetAsync("/ai/connection")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/ai/tool")).StatusCode.Should().Be(HttpStatusCode.OK);
        var deniedWrite = await client.PostAsJsonAsync("/ai/connection", new CreateAiConnectionRequestDto
        {
            Name = "Nicht erlaubt",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example",
            SecretReference = "env:FLOWZER_AI_OPENAI"
        });
        deniedWrite.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken([
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("roles", "ai-manager")
            ]));
        (await client.GetAsync("/ai/connection")).StatusCode.Should().Be(HttpStatusCode.OK);
        var allowedWrite = await client.PostAsJsonAsync("/ai/connection", new CreateAiConnectionRequestDto
        {
            Name = "Administriert",
            Provider = AiProviderKindDto.OpenAi,
            Location = AiProcessingLocationDto.Cloud,
            DefaultModel = "gpt-example",
            SecretReference = "env:FLOWZER_AI_OPENAI"
        });
        allowedWrite.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await allowedWrite.Content.ReadAsStringAsync();
        createdBody.Should().NotContain("secretReference");
        createdBody.Should().NotContain("FLOWZER_AI_OPENAI");
        using var createdJson = System.Text.Json.JsonDocument.Parse(createdBody);
        var created = createdJson.RootElement.GetProperty("result");
        var createdId = created.GetProperty("id").GetGuid();
        var createdRevision = created.GetProperty("revision").GetInt64();
        var disabled = await client.PutAsJsonAsync($"/ai/connection/{createdId}/enabled", new SetAiConnectionEnabledRequestDto
        {
            ExpectedRevision = createdRevision,
            Enabled = false
        });
        disabled.StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken([
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("roles", "ai-user")
            ]));
        var useOnlyList = (await (await client.GetAsync("/ai/connection"))
            .Content.ReadFromJsonAsync<ApiStatusResult<AiConnectionDto[]>>())!.Result!;
        useOnlyList.Should().BeEmpty();
        (await client.GetAsync($"/ai/connection/{createdId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Die optionale BFF-Fassade muss auch im offenen Entwicklungsmodus abgeschaltet
    // bleiben, statt Authentifizierung ohne registriertes Schema anzufordern.
    [TestCase("login", "GET")]
    [TestCase("session", "GET")]
    [TestCase("csrf", "GET")]
    [TestCase("logout", "POST")]
    public async Task BffEndpoints_ShouldReturnNotFound_WhenAuthenticationIsDisabled(string endpoint, string method)
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage(), "Development");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/bff/{endpoint}");

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static TestWebApplicationFactory CreateJwtFactory(TestStorage storage, string environmentName = "Production", string? requiredRole = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Scheme"] = "JwtBearer",
            ["Authentication:JwtBearer:Authority"] = Issuer,
            ["Authentication:JwtBearer:Audience"] = Audience
        };
        if (requiredRole is not null)
        {
            settings["Authentication:JwtBearer:RequiredRole"] = requiredRole;
        }

        return new TestWebApplicationFactory(storage, environmentName, settings, useStaticSigningKey: true);
    }

    private static TestWebApplicationFactory CreateBffFactory(TestStorage storage, string? bffClientSecret = "test-client-secret")
    {
        var dataProtectionPath = Path.Combine(Path.GetTempPath(), $"flowzer-bff-keys-{Guid.NewGuid():N}");
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Scheme"] = "Bff",
            ["Authentication:JwtBearer:Authority"] = Issuer,
            ["Authentication:JwtBearer:Audience"] = Audience,
            ["Authentication:JwtBearer:RequiredRole"] = "access",
            ["Authentication:JwtBearer:Roles:Modeler"] = "modeler",
            ["Authentication:JwtBearer:Roles:Operator"] = "operator",
            ["Authentication:JwtBearer:Roles:Worker"] = "worker",
            ["Authentication:JwtBearer:Roles:AiConnectionUser"] = "ai-user",
            ["Authentication:JwtBearer:Roles:AiConnectionManager"] = "ai-manager",
            ["Authentication:Bff:ClientId"] = "flowzer-console",
            ["Authentication:Bff:ClientSecret"] = bffClientSecret,
            ["Authentication:Bff:DataProtectionKeysPath"] = dataProtectionPath
        };

        return new TestWebApplicationFactory(
            storage,
            "Production",
            settings,
            useStaticSigningKey: true,
            enableBffTestEndpoints: true,
            temporaryDataProtectionPath: dataProtectionPath);
    }

    private static HttpClient CreateSecureCookieClient(TestWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

    private static string CreateToken(Guid userId, string issuer = Issuer, string audience = Audience)
    {
        return CreateToken([new Claim("sub", userId.ToString())], issuer, audience);
    }

    private static string CreateToken(Claim[] claims, string issuer = Issuer, string audience = Audience)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims)
        });
    }

    private sealed class TestWebApplicationFactory(
        TestStorage storage,
        string environmentName,
        IReadOnlyDictionary<string, string?>? configuration = null,
        bool useStaticSigningKey = false,
        bool enableBffTestEndpoints = false,
        string? temporaryDataProtectionPath = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, environmentName);

            // Auth und CORS werden in Program.cs zur Registrierungszeit aus builder.Configuration
            // gelesen. Werte aus ConfigureAppConfiguration sind dort noch nicht sichtbar, Host-Settings
            // (UseSetting) dagegen schon.
            builder.UseSetting("TimerScheduler:Enabled", "false");
            foreach (var entry in configuration ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(entry.Key, entry.Value);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStorageSystem>();
                services.RemoveAll<ITransactionalStorageProvider>();
                services.AddSingleton<IStorageSystem>(storage);
                services.AddSingleton<ITransactionalStorageProvider>(new TestTransactionalStorageProvider(storage));

                if (enableBffTestEndpoints)
                {
                    services.AddSingleton<BffMutationProbe>();
                    services.AddControllers().AddApplicationPart(typeof(BffTestController).Assembly);
                }

                if (useStaticSigningKey)
                {
                    // Ersetzt ausschliesslich die OIDC-Discovery (Netzwerkzugriff auf die Authority)
                    // durch statische Metadaten mit dem Testschluessel. Alles andere bleibt produktiv.
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                            new OpenIdConnectConfiguration
                            {
                                Issuer = Issuer,
                                SigningKeys = { SigningKey }
                            });
                    });

                    if (enableBffTestEndpoints)
                    {
                        services.PostConfigure<OpenIdConnectOptions>(FlowzerAuthenticationSchemes.OpenIdConnect, options =>
                        {
                            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                                new OpenIdConnectConfiguration
                                {
                                    Issuer = Issuer,
                                    AuthorizationEndpoint = $"{Issuer}/protocol/openid-connect/auth",
                                    TokenEndpoint = $"{Issuer}/protocol/openid-connect/token",
                                    EndSessionEndpoint = $"{Issuer}/protocol/openid-connect/logout",
                                    SigningKeys = { SigningKey }
                                });
                        });
                    }
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && temporaryDataProtectionPath is not null && Directory.Exists(temporaryDataProtectionPath))
            {
                Directory.Delete(temporaryDataProtectionPath, recursive: true);
            }
        }
    }

    private sealed class TestTransactionalStorageProvider(TestStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => storage;
    }

    private sealed class TestStorage : ITransactionalStorage
    {
        public Guid? LastRequestedUserTaskUserId { get; set; }

        public IDefinitionStorage DefinitionStorage => new EmptyDefinitionStorage();

        public IFolderStorage FolderStorage { get; } = new InMemoryFolderStorage();
        public IMessageSubscriptionStorage SubscriptionStorage => new EmptySubscriptionStorage(this);
        public IInstanceStorage InstanceStorage => new EmptyInstanceStorage();
        public IFormStorage FormStorage => new EmptyFormStorage();
        public IServiceTaskStorage ServiceTaskStorage { get; } = new InMemoryServiceTaskStorage();
        public IAiConnectionStorage AiConnectionStorage { get; } = new TestAiConnectionStorage();

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

    private sealed class TestAiConnectionStorage : IAiConnectionStorage
    {
        private readonly Dictionary<Guid, AiConnection> _connections = [];

        public Task<IReadOnlyList<AiConnection>> List() =>
            Task.FromResult<IReadOnlyList<AiConnection>>(_connections.Values.ToArray());

        public Task<AiConnection?> Get(Guid id) => Task.FromResult(_connections.GetValueOrDefault(id));

        public Task<AiConnectionWriteResult> TryCreate(AiConnection connection)
        {
            if (!_connections.TryAdd(connection.Id, connection))
                return Task.FromResult(new AiConnectionWriteResult(AiConnectionWriteStatus.Conflict, null, 0));
            return Task.FromResult(new AiConnectionWriteResult(
                AiConnectionWriteStatus.Written, connection, connection.Revision));
        }

        public Task<AiConnectionWriteResult> TryUpdate(AiConnection connection, long expectedRevision)
        {
            if (!_connections.TryGetValue(connection.Id, out var current))
                return Task.FromResult(new AiConnectionWriteResult(AiConnectionWriteStatus.NotFound, null, 0));
            if (current.Revision != expectedRevision)
                return Task.FromResult(new AiConnectionWriteResult(
                    AiConnectionWriteStatus.Conflict, current, current.Revision));
            _connections[connection.Id] = connection;
            return Task.FromResult(new AiConnectionWriteResult(
                AiConnectionWriteStatus.Written, connection, connection.Revision));
        }
    }

    private sealed class EmptyDefinitionStorage : IDefinitionStorage
    {
        public Task StoreBinary(Guid guid, string data) => throw new NotSupportedException();
        public Task<string> GetBinary(Guid guid) => throw new NotSupportedException();
        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(Array.Empty<Guid>());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(Array.Empty<BpmnDefinition>());
        public Task StoreDefinition(BpmnDefinition definition) => throw new NotSupportedException();
        public Task<Model.Version?> GetMaxVersionId(string modelId) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();
        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) => throw new NotSupportedException();
        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => throw new NotSupportedException();
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => throw new NotSupportedException();
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new NotSupportedException();
    }

    private sealed class EmptySubscriptionStorage(TestStorage storage) : IMessageSubscriptionStorage
    {
        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task AddMessageSubscription(MessageSubscription messageSubscription) => Task.CompletedTask;
        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId) => Task.CompletedTask;
        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;

        public void AddSignalSubscription(SignalSubscription signalSubscription)
        {
        }

        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<SignalSubscription>());

        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId)
        {
        }

        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) => Task.FromResult(Enumerable.Empty<UserTaskSubscription>());

        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId)
        {
            storage.LastRequestedUserTaskUserId = userId;
            return Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());
        }

        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;
        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;

        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId)
        {
        }

        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task AddTimerSubscription(TimerSubscription timerSubscription) => Task.CompletedTask;
        public Task RemoveTimerSubscription(Guid timerSubscriptionId) => Task.CompletedTask;
        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class EmptyInstanceStorage : IInstanceStorage
    {
        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) => throw new FileNotFoundException();
        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo) => Task.CompletedTask;
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() => Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() => Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
    }

    private sealed class EmptyFormStorage : IFormStorage
    {
        public Task SaveFormMetaData(FormMetadata formMetadata) => Task.CompletedTask;
        public Task<FormMetadata> GetFormMetaData(Guid formId) => throw new FileNotFoundException();
        public Task<IEnumerable<FormMetadata>> GetFormMetadatas() => Task.FromResult(Enumerable.Empty<FormMetadata>());
        public Task UpdateFormMetaData(FormMetadata formMetaData) => Task.CompletedTask;
        public Task DeleteFormMetaData(Guid formId) => Task.CompletedTask;
        public Task SaveForm(Form form) => Task.CompletedTask;
        public Task<Form> GetForm(Guid id) => throw new FileNotFoundException();
        public Task<IEnumerable<Form>> GetForms(Guid formId) => Task.FromResult(Enumerable.Empty<Form>());
        public Task DeleteForm(Guid id) => Task.CompletedTask;
        public Task<Model.Version> GetMaxVersion(Guid formId) => Task.FromResult(new Model.Version());
    }
}

/// <summary>Nur im Integrationstest registrierte Endpunkte fuer Cookie- und CSRF-Nachweise.</summary>
[ApiController]
[Route("__tests/bff")]
public sealed class BffTestController(BffMutationProbe mutationProbe) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("signin")]
    public async Task<IActionResult> SignInForTest([FromQuery] Guid userId, [FromQuery] bool hasAccess = true)
    {
        Claim[] claims =
        [
            new("iss", IssuerForTest),
            new("sub", userId.ToString()),
            new("name", "Ada Lovelace"),
            new("email", "ada@example.test"),
            new("groups", "/engineering"),
            new("roles", "modeler"),
            new("roles", "operator"),
            new("roles", "worker"),
            new("roles", "ai-user"),
            new("roles", "ai-manager"),
            new("resource_access", "{\"flowzer-api\":{\"roles\":[\"access\"]}}", JsonClaimValueTypes.Json)
        ];
        await HttpContext.SignInAsync("Flowzer.Cookie", new ClaimsPrincipal(new ClaimsIdentity(
            hasAccess ? claims : claims.Where(claim => claim.Type is not "roles" and not "resource_access"), "test")));
        return NoContent();
    }

    [Authorize(Policy = WebApiEngine.Auth.FlowzerPolicies.Access)]
    [HttpPost("mutate")]
    public IActionResult Mutate()
    {
        mutationProbe.Executions++;
        return NoContent();
    }

    private const string IssuerForTest = "https://issuer.test/realms/flowzer";
}

public sealed class BffMutationProbe
{
    public int Executions { get; set; }
}
