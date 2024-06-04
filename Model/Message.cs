using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Model;

public record Message
{
    public required string Name { get; init; }
    public string? CorrelationKey { get; init; }
    
    [JsonConverter(typeof(JsonStringConverter))]
    public string? Variables { get; init; }
    public int TimeToLive { get; init; } = 3600;
}


public class JsonStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            
            case JsonTokenType.StartObject:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return doc.RootElement.GetRawText();
            }
            
            default:
                throw new JsonException();
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        using (var doc = JsonDocument.Parse(value))
        {
            doc.WriteTo(writer);
        }
    }
}

