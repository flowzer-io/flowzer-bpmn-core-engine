using System.Text.Json;
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
            if (Text(calculation, "name") != "join.v1" || sources.ValueKind != JsonValueKind.Array
                || sources.GetArrayLength() is < 1 or > 20 || field.Conditions.Count > 0
                || field.Type is not ("hidden" or "textfield" or "textarea")) Fail();
            foreach (var source in sources.EnumerateArray())
                if (source.ValueKind != JsonValueKind.String || !fields.Any(candidate => candidate.Key == source.GetString()
                    && !IsCalculated(candidate) && !True(candidate.Schema, "multiple"))) Fail();
        }
    }

    public static string Compute(FormField field, FormContract contract, JsonElement input, JsonElement context)
    {
        var calculation = Get(Get(field.Schema, "flowzer"), "calculation");
        List<string> parts = [];
        foreach (var source in Get(calculation, "fields").EnumerateArray())
        {
            var definition = contract.Fields.Single(candidate => candidate.Key == source.GetString());
            var value = Get(definition.ReadOnly ? context : input, definition.Key);
            if (value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                throw new FormSubmissionException(new Dictionary<string, string[]> { [field.Key] = ["calculation.input_type"] });
            if (value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) && !string.IsNullOrWhiteSpace(value.ToString())) parts.Add(value.ToString());
        }
        var result = string.Join(" · ", parts);
        if (result.Length > 131072) throw new FormSubmissionException(new Dictionary<string, string[]> { [field.Key] = ["calculation.limit"] });
        return result;
    }

    private static void Fail() => throw new InvalidOperationException("Unsupported form contract: calculation.unsupported.");
}
