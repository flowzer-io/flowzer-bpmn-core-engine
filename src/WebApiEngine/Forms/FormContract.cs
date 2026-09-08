using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Forms;

/// <summary>
/// Stabiler, maschinenlesbarer Grund fuer ein inkompatibles Formularschema.
/// Die Nachricht bleibt absichtlich generisch, damit Schema-Details den Server
/// ueber Kompatibilitaetsberichte nie verlassen.
/// </summary>
public sealed class FormContractException(string code)
    : InvalidOperationException("Unsupported form contract: " + code + ".")
{
    public string Code { get; } = string.IsNullOrWhiteSpace(code) ? "schema.invalid" : code;
}

/// <summary>Explizites, begrenztes Prüfprofil; unbekannte Regeln sind keine Freigabe.</summary>
public sealed record FormContract(
    string ValidationProfile,
    IReadOnlyList<FormField> Fields,
    IReadOnlySet<string> IgnoredKeys,
    JsonElement Rules)
{
    public const string ProfileV1 = "flowzer.forms/1";
    public const string ProfileV2 = "flowzer.forms/2";
    public const string ProfileV3 = "flowzer.forms/3";

    /// <summary>
    /// Begrenzte, echte Array-von-Objekten-Strukturen. Sie bleiben getrennt von den
    /// flachen Feldern, damit bestehende Profile nicht unbemerkt Objektwerte freigeben.
    /// </summary>
    public IReadOnlyList<FormRepeatGroup> RepeatGroups { get; init; } = [];

    public static bool IsSupportedProfile(string? profile) =>
        profile is null or ProfileV1 or ProfileV2 or ProfileV3;
}

public sealed record FormField(
    string Key,
    string Type,
    JsonElement Schema,
    bool ReadOnly,
    IReadOnlyList<JsonElement> Conditions,
    DirectorySubjectSelectionPolicy? SubjectSelection = null);

public sealed record FormRepeatGroup(
    string Key,
    JsonElement Schema,
    bool ReadOnly,
    IReadOnlyList<JsonElement> Conditions,
    IReadOnlyList<FormField> Fields,
    int MinItems,
    int MaxItems);

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
        throw new FormContractException("validation.numeric_constraint");
    }
    internal static bool SafeKey(string key) => key.Length is > 0 and <= 128
        && Regex.IsMatch(key, "^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)
        && key is not "UserId" and not "constructor" and not "prototype";
}
