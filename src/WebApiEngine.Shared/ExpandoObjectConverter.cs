using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

public class ExpandoObjectConverter: JsonConverter<ExpandoObject>
{
    public override ExpandoObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement.Clone();
        var rawText = root.GetRawText();
        var ret = Newtonsoft.Json.JsonConvert.DeserializeObject<ExpandoObject>(rawText, new Newtonsoft.Json.Converters.ExpandoObjectConverter());
        return ret;
    }

    public override void Write(Utf8JsonWriter writer, ExpandoObject value, JsonSerializerOptions options)
    {
        var rawText = Newtonsoft.Json.JsonConvert.SerializeObject(value, new Newtonsoft.Json.Converters.ExpandoObjectConverter());
        using var doc = JsonDocument.Parse(rawText);
        doc.WriteTo(writer);
    }
}