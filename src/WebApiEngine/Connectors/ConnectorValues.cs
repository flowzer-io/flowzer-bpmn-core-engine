using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Connectors;

/// <summary>
/// Liest die Eingaben eines Auftrags und baut die Ergebnisvariablen.
///
/// Prozessvariablen sind lose typisiert: Je nach Weg stehen dort einfache CLR-Werte, ein
/// <see cref="ExpandoObject"/> oder ein <see cref="JsonElement"/> aus einer HTTP-Antwort.
/// Diese Stelle ist bewusst nachsichtig beim Lesen und streng beim Schreiben: Zurueck in den
/// Prozess gehen nur Werte, die die Ablage unbeschaedigt speichert — sonst landete aus einer
/// JSON-Antwort nur <c>{"ValueKind": 3}</c> im Vorgang.
/// </summary>
internal static class ConnectorValues
{
    public static IDictionary<string, object?> Read(Variables? variables) =>
        variables is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : (IDictionary<string, object?>)variables;

    public static object? Get(IDictionary<string, object?> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    public static string? AsString(object? value) => value switch
    {
        null => null,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement element => element.GetRawText(),
        JsonNode node => node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString(),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    public static bool? AsBoolean(object? value) => value switch
    {
        null => null,
        bool flag => flag,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        string text when bool.TryParse(text, out var parsed) => parsed,
        JsonElement { ValueKind: JsonValueKind.String } element when bool.TryParse(element.GetString(), out var parsed) => parsed,
        _ => null
    };

    public static int? AsInteger(object? value) => value switch
    {
        null => null,
        int number => number,
        long number => (int)Math.Clamp(number, int.MinValue, int.MaxValue),
        double number => (int)Math.Clamp(number, int.MinValue, int.MaxValue),
        decimal number => (int)Math.Clamp(number, int.MinValue, int.MaxValue),
        JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var parsed) =>
            (int)Math.Clamp(parsed, int.MinValue, int.MaxValue),
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
            (int)Math.Clamp(parsed, int.MinValue, int.MaxValue),
        _ => null
    };

    /// <summary>
    /// Ein Objekt aus dem Auftrag als flache Textabbildung, etwa fuer Header oder Query.
    /// Verschachtelte Werte werden als JSON-Text uebernommen, statt still zu verschwinden.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> AsTextPairs(object? value)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var entry in EnumerateObject(value))
        {
            var text = AsString(entry.Value);
            if (text is not null)
            {
                pairs.Add(new KeyValuePair<string, string>(entry.Key, text));
            }
        }

        return pairs;
    }

    /// <summary>Ein einzelner Text oder eine Liste von Texten, etwa Empfaengeradressen.</summary>
    public static IReadOnlyList<string> AsTextList(object? value)
    {
        switch (value)
        {
            case null:
                return [];
            case string text:
                return string.IsNullOrWhiteSpace(text) ? [] : [text.Trim()];
            case JsonElement { ValueKind: JsonValueKind.Array } element:
                return element.EnumerateArray()
                    .Select(item => AsString(item)?.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!)
                    .ToList();
            case IEnumerable<object?> items:
                return items
                    .Select(item => AsString(item)?.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!)
                    .ToList();
        }

        var single = AsString(value)?.Trim();
        return string.IsNullOrWhiteSpace(single) ? [] : [single];
    }

    /// <summary>
    /// Der Nachrichtenkoerper: Ein Text geht so hinaus, wie er dasteht; alles andere wird JSON.
    /// </summary>
    public static (string? Content, bool IsJson) AsRequestBody(object? value)
    {
        switch (value)
        {
            case null:
                return (null, false);
            case string text:
                return (text, false);
            case JsonElement { ValueKind: JsonValueKind.String } element:
                return (element.GetString(), false);
            case JsonElement element:
                return (element.GetRawText(), true);
            case JsonNode node:
                return (node.ToJsonString(), true);
        }

        return (Newtonsoft.Json.JsonConvert.SerializeObject(value), true);
    }

    /// <summary>
    /// Baut Prozessvariablen aus einem JSON-Text. Bewusst in einfache CLR-Werte und
    /// <see cref="ExpandoObject"/> statt in <see cref="JsonElement"/>: Von einem JsonElement
    /// speicherte die Ablage nur <c>{"ValueKind": 3}</c>, und der Wert waere im Vorgang weg.
    /// </summary>
    public static object? FromJsonText(string json)
    {
        using var document = JsonDocument.Parse(json);
        return FromElement(document.RootElement);
    }

    public static bool TryParseJsonText(string json, out object? value)
    {
        try
        {
            value = FromJsonText(json);
            return true;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
    }

    private static object? FromElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => FromObjectElement(element),
        JsonValueKind.Array => element.EnumerateArray().Select(FromElement).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    private static Variables FromObjectElement(JsonElement element)
    {
        Variables variables = new();
        var writable = (IDictionary<string, object?>)variables;
        foreach (var property in element.EnumerateObject())
        {
            writable[property.Name] = FromElement(property.Value);
        }

        return variables;
    }

    public static Variables Build(params (string Name, object? Value)[] entries)
    {
        Variables variables = new();
        var writable = (IDictionary<string, object?>)variables;
        foreach (var entry in entries)
        {
            writable[entry.Name] = entry.Value;
        }

        return variables;
    }

    public static Variables ToVariables(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        Variables variables = new();
        var writable = (IDictionary<string, object?>)variables;
        foreach (var pair in pairs)
        {
            writable[pair.Key] = pair.Value;
        }

        return variables;
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateObject(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case IDictionary<string, object?> dictionary:
                foreach (var entry in dictionary)
                {
                    yield return entry;
                }

                yield break;
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                foreach (var property in element.EnumerateObject())
                {
                    yield return new KeyValuePair<string, object?>(property.Name, property.Value);
                }

                yield break;
            case JsonObject jsonObject:
                foreach (var property in jsonObject)
                {
                    yield return new KeyValuePair<string, object?>(property.Key, property.Value);
                }

                yield break;
        }
    }

}
