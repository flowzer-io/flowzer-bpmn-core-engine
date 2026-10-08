using FilesystemStorageSystem;
using FluentAssertions;
using Model;

namespace WebApiEngine.Tests;

/// <summary>Einmaleinlösung und nur gehashte Persistenz im Datei-Entwicklungsadapter.</summary>
[NonParallelizable]
public sealed class FormEmbedGrantStorageTest
{
    // Testzweck: Ein neuer Adapterprozesszustand darf einen verbrauchten Link nicht wieder
    // aktivieren; die Dateiablage enthält nur Hash und stabile Bindung, kein Secret/Payload.
    [Test]
    public async Task FileStorage_ShouldPersistHashAndConsumeExactlyOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "flowzer-embed-store-" + Guid.NewGuid().ToString("N"));
        var oldRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, root);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var grant = Create(now.AddMinutes(5));
            var first = new Storage();
            await first.FormEmbedGrantStorage.Create(grant, now);
            (await first.FormEmbedGrantStorage.Find(grant.SecretHash, now)).Should().NotBeNull();
            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
            files.Should().ContainSingle();
            var content = await File.ReadAllTextAsync(files.Single());
            content.Should().Contain(grant.SecretHash).And.NotContain("FormData").And.NotContain("AccessToken");
            var second = new Storage();
            var results = await Task.WhenAll(first.FormEmbedGrantStorage.TryTake(grant.SecretHash, now),
                second.FormEmbedGrantStorage.TryTake(grant.SecretHash, now));
            results.Should().ContainSingle(item => item != null);
            (await new Storage().FormEmbedGrantStorage.TryTake(grant.SecretHash, now)).Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, oldRoot);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    // Testzweck: Exakt am Ablaufzeitpunkt wird kein Snapshot geliefert; die unabhängige
    // Bereinigung entfernt abgelaufene Linkdateien ohne noch gültige Einstiege anzutasten.
    [Test]
    public async Task FileStorage_ShouldEnforceExpiryAndCleanExpiredGrants()
    {
        var root = Path.Combine(Path.GetTempPath(), "flowzer-embed-expiry-" + Guid.NewGuid().ToString("N"));
        var oldRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, root);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var first = new Storage();
            var expired = Create(now);
            await first.FormEmbedGrantStorage.Create(expired, now.AddMinutes(-1));
            (await first.FormEmbedGrantStorage.TryTake(expired.SecretHash, now)).Should().BeNull();
            await first.FormEmbedGrantStorage.Create(expired, now.AddMinutes(-1));
            var live = Create(now.AddMinutes(5), new string('B', 64));
            await first.FormEmbedGrantStorage.Create(live, now);
            await first.FormEmbedGrantStorage.CleanupExpired(now);
            Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Should().ContainSingle();
            (await first.FormEmbedGrantStorage.TryTake(live.SecretHash, now)).Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, oldRoot);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    // Testzweck: Defekte Dateien in der privaten temporären Ablage blockieren keine
    // neuen Links; pro Aufgabe gibt es dieselbe kleine Obergrenze wie in PostgreSQL.
    [Test]
    public async Task FileStorage_ShouldBoundLinksAndDiscardCorruptExpiredRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "flowzer-embed-bounded-" + Guid.NewGuid().ToString("N"));
        var oldRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, root);
        try
        {
            var storage = new Storage();
            var now = DateTimeOffset.UtcNow;
            var taskId = Guid.NewGuid();
            for (var i = 0; i < 6; i++)
                await storage.FormEmbedGrantStorage.Create(Create(now.AddMinutes(i + 1), new string((char)('A' + i), 64), taskId), now);
            var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
            files.Should().HaveCount(4);
            var corrupt = Path.Combine(Path.GetDirectoryName(files[0])!, new string('F', 64) + "-corrupt.json");
            await File.WriteAllTextAsync(corrupt, "{");
            await storage.FormEmbedGrantStorage.CleanupExpired(now);
            File.Exists(corrupt).Should().BeFalse();
            (await storage.FormEmbedGrantStorage.Find(new string('A', 64), now)).Should().BeNull();
            (await storage.FormEmbedGrantStorage.Find(new string('F', 64), now)).Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, oldRoot);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static FormEmbedGrant Create(DateTimeOffset expires, string? hash = null, Guid? taskId = null) => new()
    {
        SecretHash = hash ?? new string('A', 64), UserTaskId = taskId ?? Guid.NewGuid(), DefinitionId = Guid.NewGuid(),
        OwnerUserId = Guid.NewGuid(), Owner = new AuthenticatedSubject("https://synthetic.test", "subject"),
        HostOrigin = "https://host.test", TaskRevision = 0, ExpiresAtUtc = expires
    };
}
