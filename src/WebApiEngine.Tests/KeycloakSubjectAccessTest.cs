using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Model;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

/// <summary>Live-Lesevertrag mit ausschließlich synthetischen Keycloak-HTTP-Antworten.</summary>
public sealed class KeycloakSubjectAccessTest
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string ClientId = "flowzer-api", ClientUuid = "client-internal-uuid";
    private static readonly AuthenticatedSubject Identity = new(Issuer, "user-a");

    // Testzweck: Aktuelles Einzelprofil, echte Child-IDs und effektive Clientrollen sind
    // die Autorität; kein globaler User-/Gruppenkatalog, keine Gruppenmember oder Claimrollen.
    [TestCase("root")]
    [TestCase("child")]
    [TestCase("f:provider:mäx")]
    [TestCase("f:provider:🙂")]
    public async Task Access_ShouldReadOnlyRequestedProfileScopeAndEffectiveClientRole(string variant)
    {
        var identity = variant.StartsWith("f:", StringComparison.Ordinal) ? new(Issuer, variant) : Identity;
        using var handler = new Handler(variant, identity.Subject); using var http = new HttpClient(handler);
        var allowed = await Client(http).HasCurrentAccessAsync(identity, ClientId, "access", CancellationToken.None);
        allowed.Should().BeTrue();
        handler.Requests.Should().NotContain(path => path.Contains("/members", StringComparison.Ordinal)
            || path.Contains("/users?", StringComparison.Ordinal) || path.Contains("/groups?first", StringComparison.Ordinal)
            && !path.Contains("/users/", StringComparison.Ordinal));
        handler.Requests.Should().Contain($"/admin/realms/flowzer/users/{Uri.EscapeDataString(identity.Subject)}/role-mappings/clients/{ClientUuid}/composite");
        handler.Requests.Should().NotContain(path => path.Contains($"/clients/{ClientId}/composite", StringComparison.Ordinal));
        handler.TokenRequests.Should().Be(1);
    }

    // Testzweck: Deaktivierung, tatsächliches Löschen, Gruppen- oder Rollenentzug und ein
    // deaktivierter API-Client sind sicher negative Zugänge, niemals Worker-Stellvertretung.
    [TestCase("disabled")]
    [TestCase("deleted")]
    [TestCase("outside")]
    [TestCase("no-membership")]
    [TestCase("no-role")]
    [TestCase("disabled-client")]
    public async Task Access_ShouldDenyConfirmedLoss(string variant)
    {
        using var handler = new Handler(variant); using var http = new HttpClient(handler);
        (await Client(http).HasCurrentAccessAsync(Identity, ClientId, "access", CancellationToken.None)).Should().BeFalse();
    }

    // Testzweck: HTTP-Ausfälle, falsche IDs/Pfade, mehrdeutige JSON-Quellen und fremde
    // Clientrollen sind keine bestätigten Rechteentzüge und dürfen niemals autorisieren.
    [TestCase("forbidden", KeycloakAdminClientFailureKind.Authorization)]
    [TestCase("unavailable", KeycloakAdminClientFailureKind.Transient)]
    [TestCase("missing-root", KeycloakAdminClientFailureKind.NotFound)]
    [TestCase("wrong-user", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("wrong-root", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("moved-child", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("moved-membership", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("new-child", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("duplicate-group", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("duplicate-client", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("wrong-client", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("missing-client", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("realm-role", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("foreign-role", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("duplicate-role", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("duplicate-enabled", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("duplicate-token", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("wrong-token-type", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("wrong-content-type", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("oversize", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("malformed", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("redirect", KeycloakAdminClientFailureKind.Permanent)]
    [TestCase("token-not-found", KeycloakAdminClientFailureKind.Authentication)]
    [TestCase("null-membership", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("case-duplicate-enabled", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("unsafe-token", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("chunked-oversize", KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("underreported-oversize", KeycloakAdminClientFailureKind.InvalidResponse)]
    public async Task Access_ShouldFailClosedWithoutLeakingHttpInput(string variant, KeycloakAdminClientFailureKind kind)
    {
        using var handler = new Handler(variant); using var http = new HttpClient(handler);
        var error = await FluentActions.Awaiting(() => Client(http).HasCurrentAccessAsync(Identity, ClientId, "access", CancellationToken.None))
            .Should().ThrowAsync<KeycloakAdminClientException>();
        error.Which.Kind.Should().Be(kind);
        error.Which.InnerException.Should().BeNull();
        error.Which.ToString().Should().NotContain("PRIVATE_HTTP_INPUT").And.NotContain("synthetic-only").And.NotContain("token-private");
        if (variant is "chunked-oversize" or "underreported-oversize") handler.LastStream!.BytesRead.Should().BeGreaterThan(1024);
    }

    // Testzweck: Kein realmweiter oder permissiver Fallback; fremder Issuer, deaktivierter
    // Scope, unsichere Subjects und ungebundene Rollen stoppen bereits vor der Tokenanfrage.
    [TestCase("root-empty")]
    [TestCase("disabled")]
    [TestCase("wrong-issuer")]
    [TestCase("unsafe-subject")]
    [TestCase("role-empty")]
    [TestCase("client-empty")]
    public async Task Access_ShouldRejectUnsafeConfigurationBeforeHttp(string variant)
    {
        var options = Options();
        if (variant == "root-empty") options.RootGroupId = "";
        if (variant == "disabled") options.Enabled = false;
        var identity = variant == "wrong-issuer" ? new("https://other.invalid/realms/flowzer", "user-a")
            : variant == "unsafe-subject" ? new(Issuer, "user%2Fother") : Identity;
        using var handler = new Handler("root"); using var http = new HttpClient(handler);
        var error = await FluentActions.Awaiting(() => Client(http, options).HasCurrentAccessAsync(identity,
            variant == "client-empty" ? "" : ClientId, variant == "role-empty" ? "" : "access", CancellationToken.None))
            .Should().ThrowAsync<KeycloakAdminClientException>();
        error.Which.Kind.Should().Be(KeycloakAdminClientFailureKind.Configuration); handler.Requests.Should().BeEmpty();
    }

    // Testzweck: Derselbe Client muss vor jeder Aktion neu lesen. Der zweite Rollenstand
    // widerruft den ersten ohne Token-/Verzeichnis- oder Berechtigungscache.
    [Test]
    public async Task Access_ShouldObserveRevocationOnTheNextProbe()
    {
        using var handler = new Handler("root"); using var http = new HttpClient(handler); var client = Client(http);
        (await client.HasCurrentAccessAsync(Identity, ClientId, "access", CancellationToken.None)).Should().BeTrue();
        handler.Variant = "no-role";
        (await client.HasCurrentAccessAsync(Identity, ClientId, "access", CancellationToken.None)).Should().BeFalse();
        handler.TokenRequests.Should().Be(2);
    }

    // Testzweck: Auch ein nie endender HTTP-Aufruf bleibt im Gesamtbudget einschließlich
    // Body; ein technischer Timeout ist kein bestätigter Entzug. Caller-Abbruch bleibt Abbruch.
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Access_ShouldBoundTheWholeProbeAndPreserveCallerCancellation(bool callerCancels, bool bodyHangs)
    {
        var time = new FakeTimeProvider(); using var handler = new HangingHandler(bodyHangs); using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var action = Client(http, time: time).HasCurrentAccessAsync(Identity, ClientId, "access", cancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (callerCancels) cancellation.Cancel(); else time.Advance(TimeSpan.FromSeconds(11));
        if (callerCancels) (await FluentActions.Awaiting(() => action).Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        else (await FluentActions.Awaiting(() => action).Should().ThrowAsync<KeycloakAdminClientException>()).Which.Kind.Should().Be(KeycloakAdminClientFailureKind.Transient);
    }

    private static KeycloakDirectoryOptions Options()
    {
        var options = KeycloakDirectoryScopeTest.Options(); options.MaxResponseBytes = 1024; return options;
    }
    private static KeycloakAdminClient Client(HttpClient http, KeycloakDirectoryOptions? options = null, TimeProvider? time = null) =>
        new(http, Microsoft.Extensions.Options.Options.Create(options ?? Options()), time);

    private sealed class Handler(string variant, string subject = "user-a") : HttpMessageHandler
    {
        public string Variant { get; set; } = variant;
        public List<string> Requests { get; } = [];
        public int TokenRequests { get; private set; }
        public AccessStream? LastStream { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; Requests.Add(request.RequestUri.PathAndQuery);
            var body = "{}"; var status = HttpStatusCode.OK;
            if (path.EndsWith("/token", StringComparison.Ordinal))
            {
                TokenRequests++;
                if (Variant == "token-not-found") status = HttpStatusCode.NotFound;
                body = Variant == "unsafe-token" ? "{\"access_token\":\"token-private\\r\\nPRIVATE_HTTP_INPUT\",\"expires_in\":60,\"token_type\":\"Bearer\"}"
                    : Variant == "duplicate-token" ? "{\"access_token\":\"bad\",\"access_token\":\"token-private\",\"expires_in\":60,\"token_type\":\"Bearer\"}"
                    : $"{{\"access_token\":\"token-private\",\"expires_in\":60,\"token_type\":\"{(Variant == "wrong-token-type" ? "other" : "Bearer")}\"}}";
            }
            else
            {
                request.Headers.Authorization!.ToString().Should().Be("Bearer token-private");
                if (path.EndsWith($"/users/{Uri.EscapeDataString(subject)}", StringComparison.Ordinal))
                {
                    status = Variant switch { "deleted" => HttpStatusCode.NotFound, "forbidden" => HttpStatusCode.Forbidden,
                        "unavailable" => HttpStatusCode.ServiceUnavailable, "redirect" => HttpStatusCode.TemporaryRedirect, _ => status };
                    body = Variant switch { "case-duplicate-enabled" => $"{{\"id\":\"{subject}\",\"enabled\":false,\"Enabled\":true}}",
                        "duplicate-enabled" => $"{{\"id\":\"{subject}\",\"enabled\":false,\"enabled\":true}}",
                        "oversize" => "{\"PRIVATE_HTTP_INPUT\":\"" + new string('a', 2048) + "\"}", "malformed" => "PRIVATE_HTTP_INPUT:not-json",
                        _ => $"{{\"id\":\"{(Variant == "wrong-user" ? "foreign" : subject)}\",\"enabled\":{(Variant == "disabled" ? "false" : "true")},\"username\":\"PRIVATE_HTTP_INPUT\"}}" };
                }
                else if (path.EndsWith("/groups/demo-root", StringComparison.Ordinal))
                {
                    if (Variant == "missing-root") status = HttpStatusCode.NotFound;
                    body = Group(Variant == "wrong-root" ? "foreign" : "demo-root", "Demo", "/Organisation/Demo");
                }
                else if (path.EndsWith("/groups/demo-root/children", StringComparison.Ordinal))
                    body = "[" + Group("demo-personal", "Personal", Variant == "moved-child" ? "/Outside/Personal" : "/Organisation/Demo/Personal") + "]";
                else if (path.EndsWith("/groups/demo-personal/children", StringComparison.Ordinal)) body = "[]";
                else if (path.EndsWith("/groups", StringComparison.Ordinal) && path.Contains("/users/", StringComparison.Ordinal))
                {
                    var member = Variant switch { "outside" => Group("foreign", "Outside", "/Outside"), "new-child" => Group("new", "New", "/Organisation/Demo/New"),
                        "child" => Group("demo-personal", "Personal", "/Organisation/Demo/Personal"),
                        "moved-membership" => Group("demo-root", "Demo", "/Moved"), _ => Group("demo-root", "Demo", "/Organisation/Demo") };
                    body = Variant == "null-membership" ? "[null]" : Variant == "no-membership" ? "[]" : "[" + member + (Variant == "duplicate-group" ? "," + member : "") + "]";
                }
                else if (path.EndsWith("/clients", StringComparison.Ordinal))
                {
                    request.RequestUri.Query.Should().Contain("clientId=flowzer-api");
                    var client = $"{{\"id\":\"{ClientUuid}\",\"clientId\":\"{(Variant == "wrong-client" ? "foreign" : ClientId)}\",\"enabled\":{(Variant == "disabled-client" ? "false" : "true")}}}";
                    body = Variant == "missing-client" ? "[]" : "[" + client + (Variant == "duplicate-client" ? "," + client : "") + "]";
                }
                else if (path.EndsWith($"/role-mappings/clients/{ClientUuid}/composite", StringComparison.Ordinal))
                {
                    var role = $"{{\"id\":\"role-1\",\"name\":\"access\",\"clientRole\":{(Variant == "realm-role" ? "false" : "true")},\"containerId\":\"{(Variant == "foreign-role" ? "foreign" : ClientUuid)}\"}}";
                    body = Variant == "no-role" ? "[]" : "[" + role + (Variant == "duplicate-role" ? "," + role : "") + "]";
                }
                else throw new AssertionException("Unexpected synthetic HTTP route.");
            }
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8,
                Variant == "wrong-content-type" ? "text/html" : "application/json") };
            if ((Variant is "chunked-oversize" or "underreported-oversize") && path.EndsWith($"/users/{Uri.EscapeDataString(subject)}", StringComparison.Ordinal))
            {
                LastStream = new AccessStream(Encoding.UTF8.GetBytes($"{{\"id\":\"{subject}\",\"enabled\":true,\"padding\":\"{new string('a', 2048)}\"}}"));
                response.Content = new StreamContent(LastStream);
                response.Content.Headers.ContentType = new("application/json");
                response.Content.Headers.ContentLength.Should().BeNull();
                if (Variant == "underreported-oversize") response.Content.Headers.ContentLength = 64;
            }
            return Task.FromResult(response);
        }
        private static string Group(string id, string name, string path) => $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"path\":\"{path}\"}}";
    }
    private sealed class HangingHandler(bool bodyHangs) : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (bodyHangs)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new AccessStream([], Entered)) };
                response.Content.Headers.ContentType = new("application/json"); return response;
            }
            Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); throw new InvalidOperationException();
        }
    }
    private sealed class AccessStream(byte[] bytes, TaskCompletionSource? hanging = null) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (hanging is not null) { hanging.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            var read = await _inner.ReadAsync(buffer, cancellationToken); BytesRead += read; return read;
        }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
