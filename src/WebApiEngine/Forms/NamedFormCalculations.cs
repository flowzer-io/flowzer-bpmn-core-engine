using System.Text.Json;
using System.Globalization;
using static WebApiEngine.Forms.FormJson;

namespace WebApiEngine.Forms;

/// <summary>
/// Kleine versionierte Registry rein lokaler Berechnungen. Namen sind keine Scripts,
/// Funktionen aus dem Formular oder Netzwerkziele. Weitere Einträge benötigen eigene Tests.
/// </summary>
public static class NamedFormCalculations
{
    public static bool IsCalculated(FormField field) => Active(Get(Get(field.Schema, "flowzer"), "calculation"));

    public static void ValidateContract(IReadOnlyList<FormField> fields)
    {
        foreach (var field in fields.Where(IsCalculated))
        {
            var calculation = Get(Get(field.Schema, "flowzer"), "calculation");
            var sources = Get(calculation, "fields");
            if (Text(calculation, "name") is not ("join.v1" or "leave-summary.v1") || sources.ValueKind != JsonValueKind.Array
                || sources.GetArrayLength() is < 1 or > 20 || field.Conditions.Count > 0
                || field.Type is not ("hidden" or "textfield" or "textarea")) Fail();
            if (Text(calculation, "name") == "leave-summary.v1"
                && !sources.EnumerateArray().Select(source => source.ToString()).SequenceEqual(
                    new[] { "mitarbeiter", "art", "von", "bis", "arbeitstage", "vertretung" })) Fail();
            foreach (var source in sources.EnumerateArray())
                if (source.ValueKind != JsonValueKind.String || !fields.Any(candidate => candidate.Key == source.GetString()
                    && !IsCalculated(candidate) && !True(candidate.Schema, "multiple"))) Fail();
        }
    }

    public static string Compute(FormField field, FormContract contract, JsonElement input, JsonElement context)
    {
        var calculation = Get(Get(field.Schema, "flowzer"), "calculation");
        List<string> parts = [];
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (var source in Get(calculation, "fields").EnumerateArray())
        {
            var definition = contract.Fields.Single(candidate => candidate.Key == source.GetString());
            var value = Get(definition.ReadOnly ? context : input, definition.Key);
            if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                throw new FormSubmissionException(new Dictionary<string, string[]> { [field.Key] = ["calculation.input_type"] });
            values[definition.Key] = value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "" : value.ToString();
            if (value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) && !string.IsNullOrWhiteSpace(value.ToString())) parts.Add(value.ToString());
        }
        var result = Text(calculation, "name") == "leave-summary.v1"
            ? LeaveSummary(values)
            : string.Join(" · ", parts);
        if (result.Length > 131072) throw new FormSubmissionException(new Dictionary<string, string[]> { [field.Key] = ["calculation.limit"] });
        return result;
    }

    // Exakte, rein lokale Ersatzberechnung der früher ausgelieferten Beispielvorlage.
    // Das Formular darf hier weder Code noch ein beliebiges Formatprogramm hinterlegen.
    private static string LeaveSummary(IReadOnlyDictionary<string, string> values)
    {
        string Value(string key) => values.GetValueOrDefault(key, "");
        string Date(string key) => DateTimeOffset.TryParse(Value(key), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : Value(key);
        var kind = Value("art") switch
        {
            "erholung" => "Erholungsurlaub", "sonder" => "Sonderurlaub",
            "unbezahlt" => "Unbezahlter Urlaub", _ => Value("art")
        };
        return string.Join(" · ", new[]
        {
            Value("mitarbeiter"), kind,
            Value("von").Length > 0 && Value("bis").Length > 0 ? $"{Date("von")} bis {Date("bis")}" : "",
            Value("arbeitstage") is not ("" or "0") ? $"{Value("arbeitstage")} Arbeitstage" : "",
            Value("vertretung").Length > 0 ? $"Vertretung: {Value("vertretung")}" : ""
        }.Where(value => value.Length > 0));
    }

    private static void Fail() => throw new FormContractException("calculation.unsupported");
}
