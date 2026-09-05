using Newtonsoft.Json;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Dieselben Serialisierungsregeln wie die Dateiablage, damit Instanzen mit polymorphen
/// BPMN-Elementen (Tokens) unveraendert zwischen beiden Ablagen wandern koennen.
/// </summary>
internal static class StorageJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        Formatting = Formatting.None
    };

    public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

    public static T Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, Settings)
        ?? throw new InvalidDataException($"Stored document could not be read as {typeof(T).Name}.");
}
