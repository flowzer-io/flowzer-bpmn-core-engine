using System.Dynamic;
using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Model;
using StorageSystem;
using static WebApiEngine.Forms.FormJson;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Forms;

/// <summary>Validiert ausschließlich deklarierte Eingaben und baut einen neuen Ergebnisscope.</summary>
public static class FormSubmissionValidator
{
    public static ExpandoObject Validate(
        FormContract contract,
        ExpandoObject? data,
        ExpandoObject? context = null,
        DirectorySnapshot? directorySnapshot = null)
    {
        using var inputDocument = JsonDocument.Parse(JsonSerializer.Serialize(data ?? new ExpandoObject()));
        using var contextDocument = JsonDocument.Parse(JsonSerializer.Serialize(context ?? new ExpandoObject()));
        var input = inputDocument.RootElement;
        var existing = contextDocument.RootElement;
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        ExpandoObject result = new();
        var output = (IDictionary<string, object?>)result;
        var declared = contract.Fields.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in input.EnumerateObject())
        {
            // Historischer Transportwert: niemals Geschäftsinput oder Besitznachweis.
            // Der Abschluss setzt den Akteur später ausschließlich aus dem Request-Kontext.
            if (item.Name == "UserId" || contract.IgnoredKeys.Contains(item.Name)) continue;
            if (!declared.Contains(item.Name)) Add(errors, SafeKey(item.Name) ? item.Name : "", "field.undeclared");
        }
        foreach (var field in contract.Fields)
        {
            if (NamedFormCalculations.IsCalculated(field)) continue;
            var value = Get(input, field.Key);
            if (field.ReadOnly)
            {
                if (value.ValueKind != JsonValueKind.Undefined && !EqualContext(value, Get(existing, field.Key))) Add(errors, field.Key, "field.read_only");
                continue;
            }
            if (!field.Conditions.All(condition => IsVisible(condition, input, existing, contract)))
            {
                if (!Empty(value)) Add(errors, field.Key, "field.inactive");
                continue;
            }
            ValidateField(field, value, errors, directorySnapshot);
            if (value.ValueKind != JsonValueKind.Undefined && !errors.ContainsKey(field.Key))
                output[field.Key] = Empty(value) && value.ValueKind != JsonValueKind.Array
                    ? null
                    : ToValue(field, value);
        }
        ValidateDateRules(contract, input, existing, errors);
        if (errors.Count > 0) throw new FormSubmissionException(errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
        foreach (var field in contract.Fields.Where(NamedFormCalculations.IsCalculated))
        {
            var calculated = NamedFormCalculations.Compute(field, contract, input, existing);
            var supplied = Get(input, field.Key);
            if (!Empty(supplied) && (supplied.ValueKind != JsonValueKind.String || supplied.GetString() != calculated)) Add(errors, field.Key, "field.calculated");
            ValidateField(field, JsonSerializer.SerializeToElement(calculated), errors, directorySnapshot);
            output[field.Key] = calculated;
        }
        if (errors.Count > 0) throw new FormSubmissionException(errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
        return result;
    }

    private static void ValidateField(
        FormField field,
        JsonElement value,
        Dictionary<string, List<string>> errors,
        DirectorySnapshot? directorySnapshot)
    {
        var validate = Get(field.Schema, "validate");
        if (value.ValueKind == JsonValueKind.Array && !True(field.Schema, "multiple"))
        {
            Add(errors, field.Key, "type.scalar");
            return;
        }
        if (True(field.Schema, "multiple") && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0
            && Number(validate, "minSelectedCount") is > 0) Add(errors, field.Key, "selection.min");
        if (Empty(value) || field.Type == "checkbox" && value.ValueKind == JsonValueKind.False && True(validate, "required"))
        {
            if (True(validate, "required")) Add(errors, field.Key, "required");
            return;
        }
        if (True(field.Schema, "multiple"))
        {
            if (value.ValueKind != JsonValueKind.Array) { Add(errors, field.Key, "type.array"); return; }
            var count = value.GetArrayLength();
            if (count > 500 || Number(validate, "maxSelectedCount") is { } max && count > max) Add(errors, field.Key, "selection.max");
            if (Number(validate, "minSelectedCount") is { } min && count < min) Add(errors, field.Key, "selection.min");
            HashSet<SubjectRef>? seenSubjects = field.Type == "flowzerSubject" ? [] : null;
            foreach (var item in value.EnumerateArray())
            {
                ValidateScalar(field, item, errors, directorySnapshot, seenSubjects);
            }
            return;
        }
        ValidateScalar(field, value, errors, directorySnapshot);
    }

    private static void ValidateScalar(
        FormField field,
        JsonElement value,
        Dictionary<string, List<string>> errors,
        DirectorySnapshot? directorySnapshot,
        HashSet<SubjectRef>? seenSubjects = null)
    {
        var validate = Get(field.Schema, "validate");
        if (field.Type == "flowzerSubject")
        {
            if (!DirectorySubjectValue.TryParse(value, out var subject))
            {
                Add(errors, field.Key, "type.subject_ref");
                return;
            }
            if (seenSubjects is not null && !seenSubjects.Add(subject))
                Add(errors, field.Key, "selection.duplicate");
            if (directorySnapshot is null)
                Add(errors, field.Key, "directory.unavailable");
            else if (field.SubjectSelection is null
                     || DirectorySubjectSelectionService.Resolve(directorySnapshot, subject, field.SubjectSelection) is null)
                Add(errors, field.Key, "selection.invalid");
        }
        else if (field.Type is "number" or "currency")
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)) { Add(errors, field.Key, "type.number"); return; }
            if (Number(validate, "min") is { } min && number < min) Add(errors, field.Key, "number.min");
            if (Number(validate, "max") is { } max && number > max) Add(errors, field.Key, "number.max");
        }
        else if (field.Type == "checkbox")
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Add(errors, field.Key, "type.boolean");
        }
        else if (field.Type is "select" or "radio")
        {
            var options = field.Type == "radio" ? Get(field.Schema, "values") : Get(Get(field.Schema, "data"), "values");
            if (!options.EnumerateArray().Any(option => JsonElement.DeepEquals(Get(option, "value"), value))) Add(errors, field.Key, "selection.invalid");
        }
        else if (field.Type == "hidden" && value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) { }
        else if (value.ValueKind != JsonValueKind.String) Add(errors, field.Key, "type.string");
        else ValidateText(field, value.GetString()!, validate, errors);
    }

    private static void ValidateText(FormField field, string value, JsonElement validate, Dictionary<string, List<string>> errors)
    {
        if (Number(validate, "minLength") is { } min && value.Length < min) Add(errors, field.Key, "text.min_length");
        if (value.Length > 131072 || Number(validate, "maxLength") is { } max && value.Length > max) Add(errors, field.Key, "text.max_length");
        var pattern = Text(validate, "pattern");
        if (pattern.Length > 0)
        {
            try { if (!FormContractCompiler.Pattern(pattern).IsMatch(value)) Add(errors, field.Key, "text.pattern"); }
            catch (RegexMatchTimeoutException) { Add(errors, field.Key, "text.pattern_limit"); }
        }
        if (field.Type == "email" && (!MailAddress.TryCreate(value, out var email) || email.Address != value)) Add(errors, field.Key, "text.email");
        if (field.Type == "url" && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) Add(errors, field.Key, "text.url");
        if (field.Type == "datetime" && !TryDate(value, out _)) Add(errors, field.Key, "date.invalid");
        if (field.Type == "time" && !TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) Add(errors, field.Key, "time.invalid");
    }

    private static void ValidateDateRules(FormContract contract, JsonElement input, JsonElement context, Dictionary<string, List<string>> errors)
    {
        if (contract.Rules.ValueKind != JsonValueKind.Array) return;
        foreach (var rule in contract.Rules.EnumerateArray())
        {
            var start = EffectiveValue(Text(rule, "start"), input, context, contract);
            var endKey = Text(rule, "end");
            var end = EffectiveValue(endKey, input, context, contract);
            if (start.ValueKind != JsonValueKind.String || end.ValueKind != JsonValueKind.String) continue;
            if (!TryDate(start.GetString()!, out var from) || !TryDate(end.GetString()!, out var to)) continue;
            if (from > to || from == to && !True(rule, "allowEqual")) Add(errors, endKey, "date.order");
        }
    }

    private static bool IsVisible(JsonElement condition, JsonElement input, JsonElement context, FormContract contract)
    {
        var value = EffectiveValue(Text(condition, "when"), input, context, contract);
        var expected = Get(condition, "eq");
        // Form.io speichert eq als String, auch bei Checkboxen. Keine Namens-/Scopeauflösung.
        return (ConditionText(value) == ConditionText(expected)) == True(condition, "show");
    }
    private static string ConditionText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "true", JsonValueKind.False => "false",
        JsonValueKind.Undefined or JsonValueKind.Null => "", _ => value.ToString()
    };
    private static JsonElement EffectiveValue(string key, JsonElement input, JsonElement context, FormContract contract) =>
        contract.Fields.Any(field => field.Key == key && field.ReadOnly) ? Get(context, key) : Get(input, key);
    private static bool EqualContext(JsonElement value, JsonElement context) =>
        context.ValueKind == JsonValueKind.Undefined ? Empty(value) : JsonElement.DeepEquals(value, context);
    private static bool Empty(JsonElement value) => value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
        || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())
        || value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0;
    private static bool TryDate(string value, out DateTimeOffset date) => DateTimeOffset.TryParseExact(value,
        ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    private static object? ToValue(FormField field, JsonElement value) => field.Type == "flowzerSubject"
        ? value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(NormalizeSubjectRef).ToArray()
            : NormalizeSubjectRef(value)
        : ToScalarValue(value);

    private static object? ToScalarValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(), JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.Array => value.EnumerateArray().Select(ToScalarValue).ToArray(),
        _ => null
    };

    private static Dictionary<string, object?> NormalizeSubjectRef(JsonElement value)
    {
        _ = DirectorySubjectValue.TryParse(value, out var subject);
        return DirectorySubjectValue.Normalize(subject);
    }
    private static void Add(Dictionary<string, List<string>> errors, string key, string code)
    {
        if (!errors.TryGetValue(key, out var codes)) errors[key] = codes = [];
        if (!codes.Contains(code)) codes.Add(code);
    }
}

/// <summary>Enthält nur Feldnamen und Fehlercodes, niemals Eingabewerte.</summary>
public sealed class FormSubmissionException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The form contains invalid or unsupported input.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
