using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StorageSystem;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

[TestFixture]
public sealed class IdentityDirectorySynchronizerTest
{
    // Testzweck: Ein vollständig gelesener Keycloak-Stand wird als eine Generation mit stabilen
    // Subjects, Gruppen und Mitgliedschaften atomar an die Ablage übergeben.
    [Test]
    public async Task TrySynchronizeAsync_ShouldPublishCompleteMappedSnapshot()
    {
        var storage = new RecordingStorage();
        var client = new StubClient(new KeycloakDirectorySnapshot(
            [
                new KeycloakDirectoryUser("subject-anna", true, "anna", "Anna", "Muster", ["group-team"]),
                new KeycloakDirectoryUser("subject-bert", false, "bert", null, null, [])
            ],
            [
                new KeycloakDirectoryGroup("group-root", "Organisation", "/Organisation", null),
                new KeycloakDirectoryGroup("group-team", "Team", "/Organisation/Team", "group-root")
            ]));
        var synchronizer = CreateSynchronizer(client, storage);

        var published = await synchronizer.TrySynchronizeAsync(CancellationToken.None);

        published.Should().BeTrue();
        storage.Started.Should().NotBeNull();
        storage.Published.Should().NotBeNull();
        var snapshot = storage.Published!;
        snapshot.Users.Should().ContainSingle(user => user.Subject == "subject-anna" && user.DisplayName == "Anna Muster" && user.IsActive);
        snapshot.Users.Should().ContainSingle(user => user.Subject == "subject-bert" && user.DisplayName == "bert" && !user.IsActive);
        snapshot.Groups.Should().ContainSingle(group => group.ExternalId == "group-team" && group.ParentId.HasValue);
        snapshot.Memberships.Should().ContainSingle();
    }

    // Testzweck: Providerfehler schreiben ausschließlich einen festen, tokenfreien Fehlerstatus.
    [Test]
    public async Task TrySynchronizeAsync_ShouldStoreSanitizedFailureStatus()
    {
        var storage = new RecordingStorage();
        var client = new ThrowingClient(new KeycloakAdminClientException(
            KeycloakAdminClientFailureKind.Authentication,
            "provider said sensitive-token and sensitive-client-secret"));
        var synchronizer = CreateSynchronizer(client, storage);

        var published = await synchronizer.TrySynchronizeAsync(CancellationToken.None);

        published.Should().BeFalse();
        storage.Failure.Should().Be(("authentication", "Keycloak directory synchronization failed."));
    }

    // Testzweck: Zwei gleichzeitige lokale Auslöser starten nicht zwei überlappende Synchronisationen.
    [Test]
    public async Task TrySynchronizeAsync_ShouldUseLocalSingleFlight()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new BlockingClient(entered, release);
        var synchronizer = CreateSynchronizer(client, new RecordingStorage());

        var first = synchronizer.TrySynchronizeAsync(CancellationToken.None);
        await entered.Task;
        var second = await synchronizer.TrySynchronizeAsync(CancellationToken.None);
        release.SetResult();
        await first;

        second.Should().BeFalse();
        client.Calls.Should().Be(1);
    }

    // Testzweck: Der manuelle HTTP-Ausloeser kann hoechstens einen Lauf vormerken und antwortet
    // sofort; weitere Klicks fuellen keine unbegrenzte Warteschlange.
    [Test]
    public void BackgroundService_ShouldBoundTheManualSynchronizationQueue()
    {
        var options = Options.Create(new KeycloakDirectoryOptions
        {
            Enabled = true,
            ServerUrl = "https://keycloak.example.invalid/",
            Issuer = "https://keycloak.example.invalid/realms/flowzer",
            Realm = "flowzer",
            ClientId = "directory-reader",
            ClientSecret = "runtime-only"
        });
        var synchronizer = new IdentityDirectorySynchronizer(
            new StubClient(new KeycloakDirectorySnapshot([], [])),
            new RecordingStorage(),
            options,
            TimeProvider.System,
            NullLogger<IdentityDirectorySynchronizer>.Instance);
        var backgroundService = new IdentityDirectoryBackgroundService(
            synchronizer,
            options,
            NullLogger<IdentityDirectoryBackgroundService>.Instance);

        backgroundService.RequestSynchronization().Should()
            .Be(IdentityDirectoryBackgroundService.ManualTriggerOutcome.Accepted);
        backgroundService.RequestSynchronization().Should()
            .Be(IdentityDirectoryBackgroundService.ManualTriggerOutcome.Busy);
    }

    // Testzweck: Lehnt die Ablage den Start wegen einer Lease eines anderen API-Prozesses ab,
    // wird Keycloak nicht gelesen und der administrative Ausloeser erhaelt eindeutig Busy.
    [Test]
    public async Task SynchronizeAsync_ShouldRespectTheStorageLease()
    {
        var storage = new RecordingStorage { StartAllowed = false };
        var client = new CountingClient();
        var synchronizer = CreateSynchronizer(client, storage);

        var outcome = await synchronizer.SynchronizeAsync(CancellationToken.None);

        outcome.Should().Be(IdentityDirectorySynchronizer.SynchronizationOutcome.Busy);
        client.Calls.Should().Be(0);
    }

    // Testzweck: Der stabile Benutzer-Schluessel bewahrt den OIDC-Issuer bytegenau; ein
    // abschliessender Slash darf nicht durch URI-Normalisierung verloren gehen.
    [Test]
    public async Task TrySynchronizeAsync_ShouldPreserveTheConfiguredIssuerExactly()
    {
        const string exactIssuer = "https://keycloak.example.invalid/realms/flowzer/";
        var storage = new RecordingStorage();
        var synchronizer = CreateSynchronizer(
            new StubClient(new KeycloakDirectorySnapshot(
                [new KeycloakDirectoryUser("subject", true, "user", null, null, [])], [])),
            storage,
            exactIssuer);

        (await synchronizer.TrySynchronizeAsync(CancellationToken.None)).Should().BeTrue();

        storage.Started!.Value.Issuer.Should().Be(exactIssuer);
        storage.Published!.Issuer.Should().Be(exactIssuer);
        storage.Published.Users.Single().Issuer.Should().Be(exactIssuer);
    }

    private static IdentityDirectorySynchronizer CreateSynchronizer(
        IKeycloakAdminClient client,
        IIdentityDirectoryStorage storage,
        string issuer = "https://keycloak.example.invalid/realms/flowzer") =>
        new(
            client,
            storage,
            Options.Create(new KeycloakDirectoryOptions
            {
                Enabled = true,
                ServerUrl = "https://keycloak.example.invalid/",
                Issuer = issuer,
                Realm = "flowzer"
            }),
            TimeProvider.System,
            NullLogger<IdentityDirectorySynchronizer>.Instance);

    private sealed class StubClient(KeycloakDirectorySnapshot snapshot) : IKeycloakAdminClient
    {
        public Task<KeycloakDirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class ThrowingClient(Exception exception) : IKeycloakAdminClient
    {
        public Task<KeycloakDirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromException<KeycloakDirectorySnapshot>(exception);
    }

    private sealed class BlockingClient(TaskCompletionSource entered, TaskCompletionSource release) : IKeycloakAdminClient
    {
        public int Calls { get; private set; }

        public async Task<KeycloakDirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            Calls++;
            entered.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new KeycloakDirectorySnapshot([], []);
        }
    }

    private sealed class CountingClient : IKeycloakAdminClient
    {
        public int Calls { get; private set; }
        public Task<KeycloakDirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new KeycloakDirectorySnapshot([], []));
        }
    }

    private sealed class RecordingStorage : IIdentityDirectoryStorage
    {
        public (string Issuer, Guid GenerationId)? Started { get; private set; }
        public DirectorySnapshot? Published { get; private set; }
        public (string Code, string Message)? Failure { get; private set; }
        public bool StartAllowed { get; init; } = true;

        public Task<DirectorySnapshot?> GetActiveSnapshot() => Task.FromResult<DirectorySnapshot?>(null);
        public Task<DirectorySyncStatus?> GetSyncStatus() => Task.FromResult<DirectorySyncStatus?>(null);
        public Task<bool> TryStartSync(string issuer, Guid generationId, DateTime startedAtUtc, DateTime leaseExpiresAtUtc)
        {
            Started = (issuer, generationId);
            return Task.FromResult(StartAllowed);
        }

        public Task<bool> FailSync(
            string issuer,
            Guid generationId,
            string errorCode,
            string errorMessage,
            DateTime failedAtUtc)
        {
            Failure = (errorCode, errorMessage);
            return Task.FromResult(true);
        }

        public Task PublishSnapshot(DirectorySnapshot snapshot)
        {
            Published = snapshot;
            return Task.CompletedTask;
        }
    }
}
