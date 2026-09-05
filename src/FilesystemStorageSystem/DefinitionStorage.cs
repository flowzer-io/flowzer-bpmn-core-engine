using Model;
using Newtonsoft.Json;
using StorageSystem;
using StorageSystem.Exceptions;
using Version = Model.Version;

namespace FilesystemStorageSystem;

/// <summary>
/// Persistiert BPMN-Definitionen, Binärinhalte und Metadaten dateibasiert unterhalb des konfigurierten Storage-Roots.
/// </summary>
public class DefinitionStorage : IDefinitionStorage
{
    private readonly string _binaryBasePath;
    private readonly string _basePath;
    private readonly string _metabasePath;
    private readonly Storage _storage;

    public DefinitionStorage(Storage storage)
    {
        _storage = storage;
        _binaryBasePath = storage.GetBasePath("FileStorage/Definitions/Binary");
        _basePath = storage.GetBasePath("FileStorage/Definitions");
        _metabasePath = storage.GetBasePath("FileStorage/Definitions/Meta");

    }


    public async Task StoreBinary(Guid guid, string data)
    {
        var fullFileName = GetBinaryPath(guid);
        await StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task<string> GetBinary(Guid guid)
    {
        var fullFileName = GetBinaryPath(guid);
        return File.ReadAllTextAsync(fullFileName);
    }

    public Task DeleteBinary(Guid guid)
    {
        var fullFileName = GetBinaryPath(guid);
        if (File.Exists(fullFileName))
        {
            File.Delete(fullFileName);
        }

        return Task.CompletedTask;
    }

    public Task<Guid[]> GetAllBinaryDefinitions()
    {
        if (Directory.Exists(_binaryBasePath) == false)
            Directory.CreateDirectory(_binaryBasePath);
        
        return Task.FromResult(Directory.GetFiles(_binaryBasePath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(Guid.Parse!).ToArray());
    }

    public Task<BpmnDefinition[]> GetAllDefinitions()
    {
        EnsureDirectoryExists(_basePath);
        var definitions = StorageFile.ReadExistingFiles(_basePath, "*.json")
            .Select(entry => JsonConvert.DeserializeObject<BpmnDefinition>(entry.Content)!)
            .ToArray();
        return Task.FromResult(definitions);
    }

    public Task StoreDefinition(BpmnDefinition definition)
    {
        var fullFileName = GetDefinitionPath(definition.Id);
        var data = JsonConvert.SerializeObject(definition,  _storage.NewtonSoftDefaultSettings);
        return StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task DeleteDefinition(Guid id)
    {
        var fullFileName = GetDefinitionPath(id);
        if (File.Exists(fullFileName))
        {
            File.Delete(fullFileName);
        }

        return Task.CompletedTask;
    }

    public async Task<Version?> GetMaxVersionId(string modelId)
    {
        var definitions = await GetAllDefinitions();
        var maxVersionId = definitions.Where(x => x.DefinitionId == modelId)
            .Max(x => x.Version);
        return maxVersionId;

    }

    public async Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions()
    {
        var ret = new List<ExtendedBpmnMetaDefinition>();
        
        if (Directory.Exists(_metabasePath) == false)
            Directory.CreateDirectory(_metabasePath);
        
        // Einmal alle Versionen lesen statt je Katalogeintrag erneut: Der alte Weg las die
        // gesamte Ablage n-mal und warf ausserdem, sobald ein Eintrag noch keine Version
        // hatte — dann war der komplette Katalog nicht mehr abrufbar.
        var definitionsByCatalogId = (await GetAllDefinitions())
            .GroupBy(definition => definition.DefinitionId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var (_, content) in StorageFile.ReadExistingFiles(_metabasePath, "*.json"))
        {
            var bpmnMetaDefinition = JsonConvert.DeserializeObject<ExtendedBpmnMetaDefinition>(content)!;

            // Ein frisch angelegter Eintrag hat noch keine Version, und nach einem
            // abgebrochenen Loeschen kann eine Version fehlen. Beides ist kein Fehler:
            // Der Eintrag gehoert in die Liste, nur eben ohne Versionsangaben.
            if (definitionsByCatalogId.TryGetValue(bpmnMetaDefinition.DefinitionId, out var definitions))
            {
                var latest = definitions.MaxBy(definition => definition.Version)!;
                bpmnMetaDefinition.LatestVersion = latest.Version;
                bpmnMetaDefinition.LatestVersionDateTime = latest.SavedOn;

                var deployed = definitions.SingleOrDefault(definition => definition.IsActive);
                if (deployed != null)
                {
                    bpmnMetaDefinition.DeployedId = deployed.Id;
                    bpmnMetaDefinition.DeployedVersion = deployed.Version;
                    bpmnMetaDefinition.DeployedVersionDateTime = deployed.SavedOn;
                }
            }

            ret.Add(bpmnMetaDefinition);
        }
        
        return ret.ToArray();
    }

    public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition)
    {
        var fullFileName = GetMetaDefinitionPath(metaDefinition.DefinitionId);
        if (File.Exists(fullFileName))
        {
            throw new DefinitionStorageConflictException($"Meta definition already exists for definitionId {metaDefinition.DefinitionId}");
        }
        var data = JsonConvert.SerializeObject(metaDefinition, _storage.NewtonSoftDefaultSettings);
        return StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition)
    {
        var fullFileName = GetMetaDefinitionPath(metaDefinition.DefinitionId);
        if (!File.Exists(fullFileName))
        {
            throw new DefinitionStorageNotFoundException($"No meta definition found for definitionId {metaDefinition.DefinitionId}");
        }
        var data = JsonConvert.SerializeObject(metaDefinition,_storage.NewtonSoftDefaultSettings);
        return StorageFile.WriteAllTextAtomicAsync(fullFileName, data);
    }

    public Task DeleteMetaDefinition(string definitionId)
    {
        var fullFileName = GetMetaDefinitionPath(definitionId);
        if (!File.Exists(fullFileName))
        {
            throw new DefinitionStorageNotFoundException($"No meta definition found for definitionId {definitionId}");
        }

        File.Delete(fullFileName);
        return Task.CompletedTask;
    }

    public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id)
    {
        var fullFileName = GetMetaDefinitionPath(id);
        if (!File.Exists(fullFileName))
        {
            throw new DefinitionStorageNotFoundException($"No meta definition found for definitionId {id}");
        }
        var content = File.ReadAllText(fullFileName);
        return Task.FromResult(JsonConvert.DeserializeObject<BpmnMetaDefinition>(content)!);
    }

    public async Task<BpmnDefinition> GetDefinitionById(Guid id)
    {
        var fullFileName = GetDefinitionPath(id);
        if (!File.Exists(fullFileName))
        {
            throw new DefinitionStorageNotFoundException($"No definition found for definitionId {id}");
        }
        var content = await File.ReadAllTextAsync(fullFileName);
        return JsonConvert.DeserializeObject<BpmnDefinition>(content)!;
    }

    public async Task<BpmnDefinition> GetLatestDefinition(string definitionId)
    {
        var definitions = await GetAllDefinitions();
        var latestDefinition = definitions.Where(x => x.DefinitionId == definitionId).MaxBy(x => x.Version);
        if (latestDefinition == null)
        {
            throw new DefinitionStorageNotFoundException($"No definition found for definitionId {definitionId}");
        }
        return latestDefinition;
    }

    public async Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId)
    {
        return  (await GetAllDefinitions()).SingleOrDefault(x => x.DefinitionId == definitionDefinitionId && x.IsActive);
    }

    private string GetBinaryPath(Guid guid) => Path.Combine(_binaryBasePath, $"{guid}.json");

    private string GetDefinitionPath(Guid id) => Path.Combine(_basePath, $"{id}.json");

    private string GetMetaDefinitionPath(string definitionId) =>
        Path.Combine(_metabasePath, $"{EnsureUsableAsFileName(definitionId)}.json");

    /// <summary>
    /// Die Kennung einer Definition kommt aus der Adresse eines HTTP-Aufrufs und wird hier zu
    /// einem Dateinamen. Ein Trennzeichen oder ein ".." darin zeigte auf eine Datei ausserhalb
    /// des Ordners — beim Loeschen waere das eine fremde Datei.
    /// </summary>
    private static string EnsureUsableAsFileName(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)
            || definitionId is "." or ".."
            || definitionId.AsSpan().IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) >= 0
            || definitionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"\"{definitionId}\" ist keine gueltige Kennung einer Definition.", nameof(definitionId));
        }

        return definitionId;
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }
}
