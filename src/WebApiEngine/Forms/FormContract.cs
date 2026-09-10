using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebApiEngine.Forms;

/// <summary>Explizites, begrenztes Prüfprofil; unbekannte Regeln sind keine Freigabe.</summary>
public sealed record FormContract(IReadOnlyList<FormField> Fields, IReadOnlySet<string> IgnoredKeys, JsonElement Rules)
{
    public const string Profile = "flowzer.forms/1";
}

public sealed record FormField(string Key, string Type, JsonElement Schema, bool ReadOnly, IReadOnlyList<JsonElement> Conditions);

internal static class FormJson
{
    internal static JsonElement Get(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;
    internal static string Text(JsonElement node, string key) => Get(node, key) is { ValueKind: JsonValueKind.String } value ? value.GetString()! : "";
    internal static bool True(JsonElement node, string key) => Get(node, key).ValueKind == JsonValueKind.True;
    internal static bool False(JsonElement node, string key) => Get(node, key).ValueKind == JsonValueKind.False;
    internal static bool Active(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        JsonValueKind.Number => !value.TryGetDecimal(out var number) || number != 0,
        _ => true
    };
    internal static decimal? Number(JsonElement node, string key)
    {
        var value = Get(node, key);
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.GetString() == "") return null;
        if (decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        throw new InvalidOperationException("Unsupported form contract: invalid numeric constraint.");
    }
    internal static bool SafeKey(string key) => key.Length is > 0 and <= 128
        && Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)
        && key is not "UserId" and not "constructor" and not "prototype";
}
