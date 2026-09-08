using System.Dynamic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        DirectorySnapshot? directorySnapshot = null,
        string? actionId = null,
        bool allowActions = false)
    {
        using var submittedDocument = JsonDocument.Parse(JsonSerializer.Serialize(data ?? new ExpandoObject()));
        var inputValue = ApplyAction(contract, submittedDocument.RootElement, actionId, allowActions);
        using var inputDocument = JsonDocument.Parse(inputValue.GetRawText());
        using var contextDocument = JsonDocument.Parse(JsonSerializer.Serialize(context ?? new ExpandoObject()));
        var input = inputDocument.RootElement;
        var existing = contextDocument.RootElement;
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        ExpandoObject result = new();
        var output = (IDictionary<string, object?>)result;
        var declared = contract.Fields.Select(field => field.Key)
            .Concat(contract.RepeatGroups.Select(group => group.Key))
            .ToHashSet(StringComparer.Ordinal);
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
        foreach (var group in contract.RepeatGroups)
            ValidateRepeatGroup(group, input, existing, contract, errors, output, directorySnapshot);
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

    private static JsonElement ApplyAction(
        FormContract contract,
        JsonElement submitted,
        string? actionId,
        bool allowActions)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        if (contract.Actions.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(actionId)) Add(errors, "", "action.invalid");
            if (errors.Count > 0) Throw(errors);
            return submitted.Clone();
        }
        if (!allowActions)
        {
            Add(errors, "", "action.not_allowed");
            Throw(errors);
        }
        if (string.IsNullOrWhiteSpace(actionId))
        {
            Add(errors, "", "action.required");
            Throw(errors);
        }
        var action = contract.Actions.SingleOrDefault(candidate => candidate.Id == actionId);
        if (action is null)
        {
            Add(errors, "", "action.invalid");
            Throw(errors);
        }

        var effective = JsonNode.Parse(submitted.GetRawText())?.AsObject()
            ?? throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["form.input_object"] });
        foreach (var assignment in action.Assignments)
        {
            var supplied = Get(submitted, assignment.Field);
            if (supplied.ValueKind != JsonValueKind.Undefined
                && !JsonElement.DeepEquals(supplied, assignment.Value))
            {
                Add(errors, assignment.Field, "action.conflict");
                continue;
            }
            effective[assignment.Field] = JsonNode.Parse(assignment.Value.GetRawText());
        }
        if (errors.Count > 0) Throw(errors);
        return JsonSerializer.SerializeToElement(effective);
    }

    [DoesNotReturn]
    private static void Throw(Dictionary<string, List<string>> errors) =>
        throw new FormSubmissionException(errors.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));

    private static void ValidateRepeatGroup(
        FormRepeatGroup group,
        JsonElement input,
        JsonElement context,
        FormContract contract,
        Dictionary<string, List<string>> errors,
        IDictionary<string, object?> output,
        DirectorySnapshot? directorySnapshot)
    {
        var value = Get(input, group.Key);
        if (group.ReadOnly)
        {
            if (value.ValueKind != JsonValueKind.Undefined
                && !EqualContext(value, Get(context, group.Key)))
                Add(errors, group.Key, "field.read_only");
            return;
        }
        if (!group.Conditions.All(condition => IsVisible(condition, input, context, contract)))
        {
            if (!Empty(value)) Add(errors, group.Key, "field.inactive");
            return;
        }
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (group.MinItems > 0) Add(errors, group.Key, "repeat.min");
            return;
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            Add(errors, group.Key, "repeat.array");
            return;
        }
        var count = value.GetArrayLength();
        if (count < group.MinItems) Add(errors, group.Key, "repeat.min");
        if (count > group.MaxItems) Add(errors, group.Key, "repeat.max");
        if (count > group.MaxItems) return;

        List<Dictionary<string, object?>> normalizedRows = [];
        var fields = group.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var rowPath = $"{group.Key}[{index}]";
            if (row.ValueKind != JsonValueKind.Object)
            {
                Add(errors, rowPath, "repeat.row_object");
                index++;
                continue;
            }

            foreach (var property in row.EnumerateObject())
                if (!fields.ContainsKey(property.Name))
                    Add(errors, $"{rowPath}.{(SafeKey(property.Name) ? property.Name : string.Empty)}", "field.undeclared");

            Dictionary<string, object?> normalized = new(StringComparer.Ordinal);
            foreach (var field in group.Fields)
            {
                var fieldPath = $"{rowPath}.{field.Key}";
                var fieldValue = Get(row, field.Key);
                if (!field.Conditions.All(condition => IsVisible(condition, row, default, group.Fields)))
                {
                    if (!Empty(fieldValue)) Add(errors, fieldPath, "field.inactive");
                    continue;
                }
                ValidateField(field, fieldValue, errors, directorySnapshot, fieldPath);
                if (fieldValue.ValueKind != JsonValueKind.Undefined && !errors.ContainsKey(fieldPath))
                    normalized[field.Key] = Empty(fieldValue) && fieldValue.ValueKind != JsonValueKind.Array
                        ? null
                        : ToValue(field, fieldValue);
            }
            normalizedRows.Add(normalized);
            index++;
        }
        output[group.Key] = normalizedRows;
    }

    private static void ValidateField(
        FormField field,
        JsonElement value,
        Dictionary<string, List<string>> errors,
        DirectorySnapshot? directorySnapshot,
        string? errorKey = null)
    {
        var key = errorKey ?? field.Key;
        var validate = Get(field.Schema, "validate");
        if (value.ValueKind == JsonValueKind.Array && !True(field.Schema, "multiple"))
        {
            Add(errors, key, "type.scalar");
            return;
        }
        if (True(field.Schema, "multiple") && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0
            && Number(validate, "minSelectedCount") is > 0) Add(errors, key, "selection.min");
        if (Empty(value) || field.Type == "checkbox" && value.ValueKind == JsonValueKind.False && True(validate, "required"))
        {
            if (True(validate, "required")) Add(errors, key, "required");
            return;
        }
        if (True(field.Schema, "multiple"))
        {
            if (value.ValueKind != JsonValueKind.Array) { Add(errors, key, "type.array"); return; }
            var count = value.GetArrayLength();
            if (count > 500 || Number(validate, "maxSelectedCount") is { } max && count > max) Add(errors, key, "selection.max");
            if (Number(validate, "minSelectedCount") is { } min && count < min) Add(errors, key, "selection.min");
            HashSet<SubjectRef>? seenSubjects = field.Type == "flowzerSubject" ? [] : null;
            foreach (var item in value.EnumerateArray())
            {
                ValidateScalar(field, item, errors, directorySnapshot, seenSubjects, key);
            }
            return;
        }
        ValidateScalar(field, value, errors, directorySnapshot, errorKey: key);
    }

    /// <summary>
    /// Prüft beim Kompilieren, dass eine feste Aktionsbelegung nicht erst jede spätere
    /// Ausführung unbrauchbar macht. Directory-Werte sind für Aktionen ohnehin gesperrt.
    /// </summary>
    internal static bool IsValidFixedActionValue(FormField field, JsonElement value)
    {
        Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);
        ValidateField(field, value, errors, directorySnapshot: null);
        return errors.Count == 0;
    }

    private static void ValidateScalar(
        FormField field,
        JsonElement value,
        Dictionary<string, List<string>> errors,
        DirectorySnapshot? directorySnapshot,
        HashSet<SubjectRef>? seenSubjects = null,
        string? errorKey = null)
    {
        var key = errorKey ?? field.Key;
        var validate = Get(field.Schema, "validate");
        if (field.Type == "flowzerSubject")
        {
            if (!DirectorySubjectValue.TryParse(value, out var subject))
            {
                Add(errors, key, "type.subject_ref");
                return;
            }
            if (seenSubjects is not null && !seenSubjects.Add(subject))
                Add(errors, key, "selection.duplicate");
            if (directorySnapshot is null)
                Add(errors, key, "directory.unavailable");
            else if (field.SubjectSelection is null
                     || DirectorySubjectSelectionService.Resolve(directorySnapshot, subject, field.SubjectSelection) is null)
                Add(errors, key, "selection.invalid");
        }
        else if (field.Type is "number" or "currency")
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)) { Add(errors, key, "type.number"); return; }
            if (Number(validate, "min") is { } min && number < min) Add(errors, key, "number.min");
            if (Number(validate, "max") is { } max && number > max) Add(errors, key, "number.max");
        }
        else if (field.Type == "checkbox")
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Add(errors, key, "type.boolean");
        }
        else if (field.Type is "select" or "radio")
        {
            var options = field.Type == "radio" ? Get(field.Schema, "values") : Get(Get(field.Schema, "data"), "values");
            if (!options.EnumerateArray().Any(option => JsonElement.DeepEquals(Get(option, "value"), value))) Add(errors, key, "selection.invalid");
        }
        else if (field.Type == "hidden" && value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) { }
        else if (value.ValueKind != JsonValueKind.String) Add(errors, key, "type.string");
        else ValidateText(field, value.GetString()!, validate, errors, key);
    }

    private static void ValidateText(FormField field, string value, JsonElement validate, Dictionary<string, List<string>> errors, string? errorKey = null)
    {
        var key = errorKey ?? field.Key;
        if (Number(validate, "minLength") is { } min && value.Length < min) Add(errors, key, "text.min_length");
        if (value.Length > 131072 || Number(validate, "maxLength") is { } max && value.Length > max) Add(errors, key, "text.max_length");
        var pattern = Text(validate, "pattern");
        if (pattern.Length > 0)
        {
            try { if (!FormContractCompiler.Pattern(pattern).IsMatch(value)) Add(errors, key, "text.pattern"); }
            catch (RegexMatchTimeoutException) { Add(errors, key, "text.pattern_limit"); }
        }
        if (field.Type == "email" && (!MailAddress.TryCreate(value, out var email) || email.Address != value)) Add(errors, key, "text.email");
        if (field.Type == "url" && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) Add(errors, key, "text.url");
        if (field.Type == "datetime" && !TryDate(value, out _)) Add(errors, key, "date.invalid");
        if (field.Type == "time" && !TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) Add(errors, key, "time.invalid");
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
        => IsVisible(condition, input, context, contract.Fields);

    private static bool IsVisible(
        JsonElement condition,
        JsonElement input,
        JsonElement context,
        IReadOnlyList<FormField> fields)
    {
        var value = EffectiveValue(Text(condition, "when"), input, context, fields);
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
        EffectiveValue(key, input, context, contract.Fields);
    private static JsonElement EffectiveValue(string key, JsonElement input, JsonElement context, IReadOnlyList<FormField> fields) =>
        fields.Any(field => field.Key == key && field.ReadOnly) ? Get(context, key) : Get(input, key);
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
