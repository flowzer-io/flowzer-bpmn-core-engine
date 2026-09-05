using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace PostgreSqlStorageSystem;

/// <summary>
/// Dieselben Serialisierungsregeln wie die Dateiablage, damit Instanzen mit polymorphen
/// BPMN-Elementen (Tokens) unveraendert zwischen beiden Ablagen wandern koennen. Beim Lesen
/// duerfen nur Typen aus den Flowzer-Assemblies und den .NET-Basisbibliotheken entstehen;
/// ein fremder <c>$type</c>-Wert in der Datenbank wird abgelehnt statt instanziiert.
/// </summary>
internal static class StorageJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        SerializationBinder = new KnownAssembliesBinder(),
        Formatting = Formatting.None
    };

    public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

    public static T Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, Settings)
        ?? throw new InvalidDataException($"Stored document could not be read as {typeof(T).Name}.");

    private sealed class KnownAssembliesBinder : DefaultSerializationBinder
    {
        private static readonly HashSet<string> AllowedAssemblies = new(StringComparer.Ordinal)
        {
            "FlowzerBPMN", "Model", "StorageSystemShared", "core-engine", "Flowzer.Shared",
            "System.Private.CoreLib", "System.Collections", "System.Linq", "System.Runtime", "mscorlib", "netstandard"
        };

        public override Type BindToType(string? assemblyName, string typeName)
        {
            var simpleAssemblyName = assemblyName?.Split(',')[0].Trim();
            if (simpleAssemblyName is null || !AllowedAssemblies.Contains(simpleAssemblyName))
            {
                throw new JsonSerializationException($"Type '{typeName}' from assembly '{assemblyName}' is not allowed in stored documents.");
            }

            var type = base.BindToType(assemblyName, typeName);
            foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
            {
                var argumentAssembly = argument.Assembly.GetName().Name;
                if (argumentAssembly is null || !AllowedAssemblies.Contains(argumentAssembly))
                {
                    throw new JsonSerializationException($"Generic argument '{argument.FullName}' is not allowed in stored documents.");
                }
            }

            return type;
        }
    }
}
