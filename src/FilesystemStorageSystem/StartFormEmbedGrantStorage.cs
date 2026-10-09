using System.Text.Json;
using Model;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>Atomarer persönlicher Start-Einstieg im Einzelprozess-Entwicklungsbetrieb.</summary>
internal sealed class StartFormEmbedGrantStorage(Storage storage) : IStartFormEmbedGrantStorage
{
    // Eine kurze Prozesssperre statt einer wachsenden Sperrentabelle je Secret.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", "StartFormEmbedGrants"));

    public async Task Create(StartFormEmbedGrant grant, DateTimeOffset utcNow)
    {
        await Gate.WaitAsync();
        try
        {
            var own = new List<StartFormEmbedGrant>();
            foreach (var file in Directory.EnumerateFiles(_path, "*.json"))
            {
                var old = await Read(file, default);
                if (old?.OwnerKey == grant.OwnerKey && old.DefinitionId == grant.DefinitionId) own.Add(old);
            }
            foreach (var old in own.OrderByDescending(g => g.ExpiresAtUtc).ThenBy(g => g.SecretHash).Skip(3))
                StorageFile.DeleteIfExists(FileOf(old.SecretHash));
            // Kein TypeNameHandling an dieser rein konkreten Dokumentgrenze.
            await StorageFile.WriteAllTextNewAtomicAsync(FileOf(grant.SecretHash), JsonSerializer.Serialize(grant));
        }
        finally { Gate.Release(); }
    }

    public async Task<StartFormEmbedGrant?> Find(string secretHash, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var grant = await Read(FileOf(secretHash), cancellationToken);
        return grant?.ExpiresAtUtc > utcNow ? grant : null;
    }

    public async Task CleanupExpired(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_path, "*.json"))
            {
                var grant = await Read(file, cancellationToken);
                if (grant is null || grant.ExpiresAtUtc <= utcNow) StorageFile.DeleteIfExists(file);
            }
        }
        finally { Gate.Release(); }
    }

    private static async Task<StartFormEmbedGrant?> Read(string file, CancellationToken cancellationToken)
    {
        try { return JsonSerializer.Deserialize<StartFormEmbedGrant>(await File.ReadAllTextAsync(file, cancellationToken)); }
        catch (FileNotFoundException) { return null; }
        catch (JsonException) { return null; } // Nur dieser private kurzlebige Grants-Ordner.
    }

    public async Task<StartFormEmbedGrant?> TryTake(string secretHash, DateTimeOffset utcNow)
    {
        await Gate.WaitAsync();
        try
        {
            var file = FileOf(secretHash);
            var content = await StorageFile.ReadAllTextIfExistsAsync(file);
            if (content is null) return null;
            StorageFile.DeleteIfExists(file);
            var grant = JsonSerializer.Deserialize<StartFormEmbedGrant>(content);
            return grant?.ExpiresAtUtc > utcNow ? grant : null;
        }
        finally { Gate.Release(); }
    }

    private string FileOf(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid form-link hash.", nameof(hash));
        return Path.Combine(_path, hash + ".json");
    }
}
