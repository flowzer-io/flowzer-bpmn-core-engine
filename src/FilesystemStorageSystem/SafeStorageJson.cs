using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FilesystemStorageSystem;

/// <summary>
/// Serializer fuer konkrete, nicht polymorphe Dateidokumente. CLR-Typnamen werden weder
/// geschrieben noch ausgewertet; polymorphe Runtime-Graphen verwenden weiterhin den
/// gesonderten Legacy-Serializer und duerfen nicht ueber diesen Vertrag hinzukommen.
/// </summary>
internal static class SafeStorageJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        Formatting = Formatting.Indented,
        MaxDepth = 128
    };

    public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, Settings);

    public static T Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, Settings)
        ?? throw new InvalidDataException($"Stored document could not be read as {typeof(T).Name}.");

    public static JObject ParseObject(string json)
    {
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text)
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 128
        };
        return JObject.Load(reader);
    }
}
