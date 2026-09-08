using System.Dynamic;
using System.Text;
using System.Text.Json;

namespace WebApiEngine.Forms;

/// <summary>
/// Begrenzt unvollstaendige Entwuerfe auf deklarierte, beschreibbare Felder. Fachliche
/// Pflicht-, Bereichs- und Auswahlpruefungen bleiben bewusst dem Abschluss vorbehalten.
/// </summary>
public static class FormDraftProjector
{
    public const int DefaultMaxPayloadBytes = 256 * 1024;

    public static string Project(
        FormContract contract,
        ExpandoObject? data,
        ExpandoObject? context = null,
        int maxPayloadBytes = DefaultMaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (maxPayloadBytes is < 1024 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

        var json = JsonSerializer.Serialize(data ?? new ExpandoObject());
        if (Encoding.UTF8.GetByteCount(json) > maxPayloadBytes)
            throw new UserTaskDraftPayloadTooLargeException(maxPayloadBytes);

        using var inputDocument = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        using var contextDocument = JsonDocument.Parse(
            JsonSerializer.Serialize(context ?? new ExpandoObject()),
            new JsonDocumentOptions { MaxDepth = 32 });
        var input = inputDocument.RootElement;
        var existing = contextDocument.RootElement;
        if (input.ValueKind != JsonValueKind.Object)
            throw Invalid("", "draft.object");

        var fields = contract.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        Dictionary<string, string[]> errors = new(StringComparer.Ordinal);
        SortedDictionary<string, JsonElement> projected = new(StringComparer.Ordinal);
        foreach (var item in input.EnumerateObject())
        {
            if (item.Name == "UserId" || contract.IgnoredKeys.Contains(item.Name)) continue;
            if (!fields.TryGetValue(item.Name, out var field))
            {
                errors[item.Name] = ["field.undeclared"];
                continue;
            }

            if (field.ReadOnly)
            {
                if (!existing.TryGetProperty(item.Name, out var contextValue)
                    || !JsonElement.DeepEquals(item.Value, contextValue))
                    errors[item.Name] = ["field.read_only"];
                continue;
            }

            // Benannte Berechnungen kommen beim Abschluss ausschliesslich vom Server. Form.io
            // darf sie anzeigen, der private Entwurf macht daraus aber keinen Eingabewert.
            if (NamedFormCalculations.IsCalculated(field)) continue;
            if (!HasSafeDraftShape(field, item.Value))
            {
                errors[item.Name] = ["draft.type"];
                continue;
            }

            projected[item.Name] = item.Value.Clone();
        }

        if (errors.Count > 0) throw new FormSubmissionException(errors);
        var result = JsonSerializer.Serialize(projected);
        if (Encoding.UTF8.GetByteCount(result) > maxPayloadBytes)
            throw new UserTaskDraftPayloadTooLargeException(maxPayloadBytes);
        return result;
    }

    private static bool HasSafeDraftShape(FormField field, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined) return false;
        var multiple = FormJson.True(field.Schema, "multiple");
        if (multiple)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 500) return false;
            return value.EnumerateArray().All(item => SafeScalar(field, item));
        }

        return value.ValueKind != JsonValueKind.Array && SafeScalar(field, value);
    }

    private static bool SafeScalar(FormField field, JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.String or JsonValueKind.Number
            or JsonValueKind.True or JsonValueKind.False
        || field.Type == "flowzerSubject"
            && value.ValueKind == JsonValueKind.Object
            && value.EnumerateObject().Count() <= 4;

    private static FormSubmissionException Invalid(string key, string code) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [code] });
}

/// <summary>Getrennter 413-Vertrag, ohne den abgelehnten Inhalt in Logs oder Antwort zu spiegeln.</summary>
public sealed class UserTaskDraftPayloadTooLargeException(int maximumBytes)
    : Exception($"The user-task draft exceeds the configured limit of {maximumBytes} bytes.");
