using Model;
using StorageSystem;
using StorageSystem.Exceptions;

namespace FilesystemStorageSystem;

/// <summary>
/// Persistiert Entscheidungsdateien dateibasiert: je Katalogeintrag eine Kopfdatei und je
/// Stand eine eigene, unveraenderliche Datei.
/// </summary>
public sealed class DecisionStorage : IDecisionStorage
{
    private readonly string _basePath;
    private readonly string _metaPath;

    public DecisionStorage(Storage storage)
    {
        _basePath = storage.GetBasePath("FileStorage/Decisions");
        _metaPath = storage.GetBasePath("FileStorage/Decisions/Meta");
    }

    public Task<IReadOnlyList<DecisionDefinition>> GetDefinitions()
    {
        IReadOnlyList<DecisionDefinition> definitions = StorageFile.ReadExistingFiles(_metaPath, "*.json")
            .Select(entry => SafeStorageJson.Deserialize<DecisionDefinition>(entry.Content))
            .OrderBy(definition => definition.DecisionDefinitionId, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(definitions);
    }

    public async Task<DecisionDefinition?> GetDefinition(string decisionDefinitionId)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(MetaFile(decisionDefinitionId));
        return content is null ? null : SafeStorageJson.Deserialize<DecisionDefinition>(content);
    }

    public Task SaveDefinition(DecisionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return StorageFile.WriteAllTextAtomicAsync(
            MetaFile(definition.DecisionDefinitionId), SafeStorageJson.Serialize(definition));
    }

    public Task DeleteDefinition(string decisionDefinitionId)
    {
        // Erst die Staende, dann der Kopf: Bricht der Vorgang dazwischen ab, bleibt ein
        // Katalogeintrag ohne Staende stehen — sichtbar und erneut loeschbar. Andersherum
        // laegen verwaiste Staende in der Ablage, die niemand mehr findet.
        foreach (var path in Directory.GetFiles(_basePath, VersionSearchPattern(decisionDefinitionId)))
        {
            StorageFile.DeleteIfExists(path);
        }

        StorageFile.DeleteIfExists(MetaFile(decisionDefinitionId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DecisionDefinitionVersion>> GetVersions(string decisionDefinitionId)
    {
        IReadOnlyList<DecisionDefinitionVersion> versions =
            StorageFile.ReadExistingFiles(_basePath, VersionSearchPattern(decisionDefinitionId))
                .Select(entry => SafeStorageJson.Deserialize<DecisionDefinitionVersion>(entry.Content))
                // Das Suchmuster trifft auch Kennungen, die mit derselben Zeichenfolge
                // beginnen ("rabatt" faende "rabatt-neu_1.json"). Die gespeicherte Kennung
                // entscheidet, nicht der Dateiname.
                .Where(version => string.Equals(
                    version.DecisionDefinitionId, decisionDefinitionId, StringComparison.Ordinal))
                .OrderBy(version => version.Version)
                .ToArray();

        return Task.FromResult(versions);
    }

    public async Task<DecisionDefinitionVersion?> GetVersion(string decisionDefinitionId, int version)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(VersionFile(decisionDefinitionId, version));
        return content is null ? null : SafeStorageJson.Deserialize<DecisionDefinitionVersion>(content);
    }

    public async Task SaveVersion(DecisionDefinitionVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        try
        {
            await StorageFile.WriteAllTextNewAtomicAsync(
                VersionFile(version.DecisionDefinitionId, version.Version), SafeStorageJson.Serialize(version));
        }
        catch (IOException)
        {
            throw new DefinitionStorageConflictException(
                $"Version {version.Version} of the decision definition '{version.DecisionDefinitionId}' already exists.");
        }
    }

    private string MetaFile(string decisionDefinitionId) =>
        Path.Combine(_metaPath, $"{EnsureUsableAsFileName(decisionDefinitionId)}.json");

    private string VersionFile(string decisionDefinitionId, int version) =>
        Path.Combine(_basePath, $"{EnsureUsableAsFileName(decisionDefinitionId)}_{version}.json");

    private static string VersionSearchPattern(string decisionDefinitionId) =>
        $"{EnsureUsableAsFileName(decisionDefinitionId)}_*.json";

    /// <summary>
    /// Der letzte Riegel unmittelbar vor dem Dateinamen — dieselbe Begruendung wie in
    /// <c>DefinitionStorage</c>: Die Kennung kommt aus der Adresse eines HTTP-Aufrufs oder aus
    /// hochgeladenem XML und wird hier zu einem Dateinamen. Ein Trennzeichen oder ein ".."
    /// darin zeigte auf eine Datei ausserhalb des Ordners; beim Loeschen waere das eine
    /// fremde Datei. Dazu kommt, was dieses Betriebssystem in Dateinamen zusaetzlich verbietet.
    /// </summary>
    private static string EnsureUsableAsFileName(string decisionDefinitionId)
    {
        DefinitionIdRules.EnsureValid(decisionDefinitionId);

        if (decisionDefinitionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                $"\"{decisionDefinitionId}\" contains characters this file system does not allow in a file name.",
                nameof(decisionDefinitionId));
        }

        return decisionDefinitionId;
    }
}
