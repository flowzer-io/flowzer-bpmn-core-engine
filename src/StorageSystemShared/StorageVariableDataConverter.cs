using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace StorageSystem;

/// <summary>
/// Schreibt dynamische Prozess-/Formulardaten als JSON-Daten, nicht als polymorphe CLR-Typen.
/// Die übrigen BPMN-/Tokenmodelle behalten ihre vorhandenen Typmetadaten und sicheren Binder.
/// Andernfalls landet bei verschachtelten SubjectRefs ein generiertes $type als drittes
/// Datenfeld im Expando-Roundtrip und macht einen zuvor gültigen Benutzerwert ungültig.
/// </summary>
public sealed class StorageVariableDataConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(ExpandoObject);

    /// <summary>Serialisiert ausschließlich den dynamischen Datenbaum ohne CLR-Typmetadaten.</summary>
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is null) { writer.WriteNull(); return; }
        var dataSerializer = JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
        JToken.FromObject(value, dataSerializer).WriteTo(writer);
    }

    /// <summary>
    /// Behält die bestehende Expando-Lesesemantik, auch für Altbestände. Ein eingereichtes
    /// $type bleibt ein gewöhnliches Datenfeld, wird niemals instanziiert oder entfernt.
    /// Der strikte SubjectRef-Parser weist solche zusätzlichen Felder weiterhin zurück.
    /// </summary>
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) =>
        new ExpandoObjectConverter().ReadJson(reader, objectType, existingValue, serializer);
}
