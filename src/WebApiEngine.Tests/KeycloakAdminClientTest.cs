using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

[TestFixture]
public sealed class KeycloakAdminClientTest
{
    // Testzweck: Der Opt-in-Abgleich akzeptiert nur getrennte sichere Admin-/Issuer-Adressen
    // und vollstaendige Laufzeit-Credentials; im ausgeschalteten Default bleiben sie optional.
    [Test]
    public void Options_ShouldFailClosedOnlyWhenSynchronizationIsEnabled()
    {
        new KeycloakDirectoryOptions().IsValid().Should().BeTrue();
        new KeycloakDirectoryOptions { Enabled = true }.IsValid().Should().BeFalse();
        new KeycloakDirectoryOptions
        {
            Enabled = true,
            ServerUrl = "https://keycloak.internal.example",
            Issuer = "https://login.example/realms/flowzer",
            Realm = "flowzer",
            ClientId = "directory-reader",
            ClientSecret = "runtime-only"
        }.IsValid().Should().BeTrue();
        new KeycloakDirectoryOptions
        {
            Enabled = true,
            ServerUrl = "https://user:password@keycloak.example",
            Issuer = "https://login.example/realms/flowzer",
            Realm = "flowzer",
            ClientId = "directory-reader",
            ClientSecret = "runtime-only"
        }.IsValid().Should().BeFalse();
    }

    // Testzweck: Der Client lädt jede paginierte Keycloak-Ressource und führt Gruppen aus
    // dem Verzeichnis und den individuellen Gruppenlisten zu einem vollständigen Snapshot zusammen.
    [Test]
    public async Task GetSnapshotAsync_ShouldFetchAllPagesAndMergeMembershipGroups()
    {
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            return (request.Method.Method, pathAndQuery) switch
            {
                ("POST", "/realms/flowzer/protocol/openid-connect/token") => Json(HttpStatusCode.OK, new { access_token = "sensitive-token", expires_in = 300 }),
                ("GET", "/admin/realms/flowzer/users?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, new object[]
                {
                    new { id = "user-a", enabled = true, username = "anna", firstName = "Anna", lastName = "Muster", email = "must-not-be-read@example.invalid" },
                    new { id = "user-b", enabled = false, username = "bert", firstName = "Bert", lastName = "Beispiel" }
                }),
                ("GET", "/admin/realms/flowzer/users?first=2&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, new[]
                {
                    new { id = "user-c", enabled = true, username = "carla" }
                }),
                ("GET", "/admin/realms/flowzer/groups?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, new[]
                {
                    new { id = "group-a", name = "Administration", path = "/Administration" },
                    new { id = "group-b", name = "Vertrieb", path = "/Vertrieb" }
                }),
                ("GET", "/admin/realms/flowzer/groups?first=2&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, new[]
                {
                    new { id = "group-c", name = "Einkauf", path = "/Einkauf" },
                    new { id = "group-extra", name = "Extern", path = "/Extern" }
                }),
                ("GET", "/admin/realms/flowzer/groups?first=4&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                ("GET", "/admin/realms/flowzer/groups/group-a/children?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                ("GET", "/admin/realms/flowzer/groups/group-b/children?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                ("GET", "/admin/realms/flowzer/groups/group-c/children?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, new[]
                {
                    new { id = "group-child-a", name = "Nord", path = "/Einkauf/Nord" },
                    new { id = "group-child-b", name = "Süd", path = "/Einkauf/Süd" }
                }),
                ("GET", "/admin/realms/flowzer/groups/group-c/children?first=2&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                ("GET", "/admin/realms/flowzer/groups/group-child-a/children?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                ("GET", "/admin/realms/flowzer/groups/group-child-b/children?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                ("GET", "/admin/realms/flowzer/groups/group-extra/children?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                ("GET", "/admin/realms/flowzer/users/user-a/groups?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, new[]
                {
                    new { id = "group-a", name = "Administration", path = "/Administration" },
                    new { id = "group-extra", name = "Extern", path = "/Extern" }
                }),
                ("GET", "/admin/realms/flowzer/users/user-a/groups?first=2&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                ("GET", "/admin/realms/flowzer/users/user-b/groups?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, new[]
                {
                    new { id = "group-b", name = "Vertrieb", path = "/Vertrieb" }
                }),
                ("GET", "/admin/realms/flowzer/users/user-c/groups?first=0&max=2&briefRepresentation=true") => Json(HttpStatusCode.OK, Array.Empty<object>()),
                _ => throw new AssertionException($"Unexpected Keycloak request: {request.Method} {pathAndQuery}")
            };
        });
        var client = CreateClient(handler, pageSize: 2);

        var snapshot = await client.GetSnapshotAsync(CancellationToken.None);

        snapshot.Users.Select(user => user.Subject).Should().Equal("user-a", "user-b", "user-c");
        snapshot.Users.Single(user => user.Subject == "user-a").Groups.Should().Equal("group-a", "group-extra");
        snapshot.Users.Single(user => user.Subject == "user-b").Enabled.Should().BeFalse();
        snapshot.Groups.Select(group => group.Id).Should().Equal("group-a", "group-b", "group-c", "group-child-a", "group-child-b", "group-extra");
        snapshot.Groups.Single(group => group.Id == "group-child-a").ParentId.Should().Be("group-c");
        handler.Requests.Should().Contain(request => request.Headers.Authorization != null && request.Headers.Authorization.Scheme == "Bearer");
    }

    // Testzweck: Vorübergehende Serverfehler werden nur innerhalb des konfigurierten Retry-Limits wiederholt.
    [Test]
    public async Task GetSnapshotAsync_ShouldRetryTransientResponsesWithinConfiguredLimit()
    {
        var attempts = 0;
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.OK, new { access_token = "sensitive-token" });
            }

            attempts++;
            return attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(HttpStatusCode.OK, Array.Empty<object>());
        });
        var client = CreateClient(handler, pageSize: 10, maxRetries: 1);

        var snapshot = await client.GetSnapshotAsync(CancellationToken.None);

        snapshot.Users.Should().BeEmpty();
        snapshot.Groups.Should().BeEmpty();
        attempts.Should().Be(3, "der erste Users-Aufruf wird wiederholt, Groups danach einmal gelesen");
    }

    // Testzweck: Fehlerhafte Client-Credentials werden als nicht wiederholbarer Authentifizierungsfehler klassifiziert.
    [Test]
    public async Task GetSnapshotAsync_ShouldClassifyInvalidClientCredentialsWithoutRetry()
    {
        var attempts = 0;
        var handler = new ScriptedHttpMessageHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        var client = CreateClient(handler, maxRetries: 3);

        var action = () => client.GetSnapshotAsync(CancellationToken.None);

        var exception = await action.Should().ThrowAsync<KeycloakAdminClientException>();
        exception.Which.Kind.Should().Be(KeycloakAdminClientFailureKind.Authentication);
        exception.Which.Message.Should().NotContain("sensitive");
        attempts.Should().Be(1);
    }

    // Testzweck: Eine nur in der Mitgliedschaft sichtbare Gruppe darf nicht ohne Eltern und
    // Kinder als scheinbare Wurzel publiziert werden; der inkonsistente Lauf wird verworfen.
    [Test]
    public async Task GetSnapshotAsync_ShouldRejectMembershipOutsideLoadedHierarchy()
    {
        var handler = new ScriptedHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/realms/flowzer/protocol/openid-connect/token" => Json(HttpStatusCode.OK, new { access_token = "token", expires_in = 300 }),
            "/admin/realms/flowzer/users" => Json(HttpStatusCode.OK, new[] { new { id = "user-a", enabled = true } }),
            "/admin/realms/flowzer/groups" => Json(HttpStatusCode.OK, Array.Empty<object>()),
            "/admin/realms/flowzer/users/user-a/groups" => Json(HttpStatusCode.OK, new[]
            {
                new { id = "late-group", name = "Late", path = "/Root/Late" }
            }),
            _ => throw new AssertionException($"Unexpected Keycloak request: {request.RequestUri}")
        });

        var action = () => CreateClient(handler).GetSnapshotAsync(CancellationToken.None);

        var exception = await action.Should().ThrowAsync<KeycloakAdminClientException>();
        exception.Which.Kind.Should().Be(KeycloakAdminClientFailureKind.InvalidResponse);
    }

    // Testzweck: Ein haengender JSON-Antwortkoerper endet innerhalb der konfigurierten Grenze
    // und wird als temporaerer Providerfehler klassifiziert statt Single-flight zu blockieren.
    [Test]
    public async Task GetSnapshotAsync_ShouldTimeoutWhileReadingResponseBody()
    {
        var handler = new ScriptedHttpMessageHandler(request => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, new { access_token = "token", expires_in = 300 })
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NeverCompletingReadStream()) });
        var client = CreateClient(handler, requestTimeoutSeconds: 1);

        var action = () => client.GetSnapshotAsync(CancellationToken.None);

        var exception = await action.Should().ThrowAsync<KeycloakAdminClientException>();
        exception.Which.Kind.Should().Be(KeycloakAdminClientFailureKind.Transient);
    }

    // Testzweck: Ein zeitlich abgebrochener HTTP-Versuch wird wie andere temporaere Fehler
    // genau innerhalb des Retry-Limits wiederholt und kann danach erfolgreich fortfahren.
    [Test]
    public async Task GetSnapshotAsync_ShouldRetryTimedOutRequest()
    {
        var handler = new TimeoutOnceHttpMessageHandler();
        var client = new KeycloakAdminClient(
            new HttpClient(handler),
            Options.Create(new KeycloakDirectoryOptions
            {
                ServerUrl = "https://keycloak.example.invalid/",
                Realm = "flowzer",
                ClientId = "flowzer-directory-sync",
                ClientSecret = "runtime-only",
                MaxRetries = 1,
                RetryDelayMilliseconds = 0,
                RequestTimeoutSeconds = 1
            }));

        var snapshot = await client.GetSnapshotAsync(CancellationToken.None);

        snapshot.Users.Should().BeEmpty();
        snapshot.Groups.Should().BeEmpty();
        handler.DirectoryRequests.Should().Be(3, "Users wird einmal wiederholt und Groups einmal gelesen");
    }

    // Testzweck: Ein Verbindungsabbruch waehrend des JSON-Bodys ist ein temporaerer
    // Transportfehler und wird nicht mit einer dauerhaften Groessenverletzung verwechselt.
    [Test]
    public async Task GetSnapshotAsync_ShouldRetryConnectionResetWhileReadingBody()
    {
        var directoryRequests = 0;
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.OK, new { access_token = "token", expires_in = 300 });
            }

            directoryRequests++;
            return directoryRequests == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ConnectionResetStream()) }
                : Json(HttpStatusCode.OK, Array.Empty<object>());
        });
        var client = CreateClient(handler, maxRetries: 1);

        var snapshot = await client.GetSnapshotAsync(CancellationToken.None);

        snapshot.Users.Should().BeEmpty();
        snapshot.Groups.Should().BeEmpty();
        directoryRequests.Should().Be(3, "Users wird einmal wiederholt und Groups einmal gelesen");
    }

    // Testzweck: Bei langen Importen wird ein kurz vor dem Ablauf stehendes Service-Token
    // erneuert, damit Pagination und Mitgliedschaften nicht dauerhaft an 401 scheitern.
    [Test]
    public async Task GetSnapshotAsync_ShouldRefreshAccessTokenBeforeExpiry()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-09-08T10:00:00Z"));
        var tokenRequests = 0;
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                tokenRequests++;
                return Json(HttpStatusCode.OK, new { access_token = $"token-{tokenRequests}", expires_in = 20 });
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/users", StringComparison.Ordinal))
            {
                time.Advance(TimeSpan.FromSeconds(16));
            }
            return Json(HttpStatusCode.OK, Array.Empty<object>());
        });
        var client = CreateClient(handler, timeProvider: time, tokenRefreshSkewSeconds: 5);

        await client.GetSnapshotAsync(CancellationToken.None);

        tokenRequests.Should().Be(2);
        handler.Requests.Where(request => request.Method == HttpMethod.Get)
            .Select(request => request.Headers.Authorization?.Parameter)
            .Should().Equal("token-1", "token-2");
    }

    private static KeycloakAdminClient CreateClient(
        ScriptedHttpMessageHandler handler,
        int pageSize = 10,
        int maxRetries = 0,
        int requestTimeoutSeconds = 30,
        TimeProvider? timeProvider = null,
        int tokenRefreshSkewSeconds = 15) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://keycloak.example.invalid/") },
            Options.Create(new KeycloakDirectoryOptions
            {
                ServerUrl = "https://keycloak.example.invalid/",
                Realm = "flowzer",
                ClientId = "flowzer-directory-sync",
                ClientSecret = "sensitive-client-secret",
                PageSize = pageSize,
                MaxRetries = maxRetries,
                RetryDelayMilliseconds = 0,
                RequestTimeoutSeconds = requestTimeoutSeconds,
                TokenRefreshSkewSeconds = tokenRefreshSkewSeconds
            }),
            timeProvider);

    private static HttpResponseMessage Json<T>(HttpStatusCode statusCode, T content) =>
        new(statusCode) { Content = JsonContent.Create(content) };

    private sealed class ScriptedHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class NeverCompletingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TimeoutOnceHttpMessageHandler : HttpMessageHandler
    {
        public int DirectoryRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.OK, new { access_token = "token", expires_in = 300 });
            }

            DirectoryRequests++;
            if (DirectoryRequests == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Json(HttpStatusCode.OK, Array.Empty<object>());
        }
    }

    private sealed class ConnectionResetStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new HttpRequestException("simulated connection reset");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new HttpRequestException("simulated connection reset"));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        private DateTimeOffset _current = current;
        public override DateTimeOffset GetUtcNow() => _current;
        public void Advance(TimeSpan duration) => _current += duration;
    }
}
