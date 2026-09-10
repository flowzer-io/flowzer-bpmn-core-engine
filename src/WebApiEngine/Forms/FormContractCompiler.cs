using System.Text.Json;
using System.Text.RegularExpressions;
using static WebApiEngine.Forms.FormJson;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Forms;

/// <summary>
/// Übersetzt gespeichertes Form.io-JSON in ein versioniertes Prüfprofil. Keine Scripts, Netzaufrufe,
/// impliziten Objektwerte oder unbekannten Geschäftsregeln. Erweiterungen brauchen Tests.
/// </summary>
public static class FormContractCompiler
{
    private static readonly HashSet<string> FieldTypes = ["textfield", "textarea", "email", "url", "phoneNumber", "password", "number", "currency", "checkbox", "radio", "select", "datetime", "time", "hidden", "flowzerSubject"];
    private static readonly HashSet<string> LayoutTypes = ["panel", "fieldset", "columns", "table", "tabs", "well"];
    private static readonly HashSet<string> ValidationKeys = ["required", "minLength", "maxLength", "min", "max", "pattern", "customMessage", "select", "minSelectedCount", "maxSelectedCount"];
    private static readonly string[] Scripts = ["calculateValue", "customDefaultValue", "customConditional", "logic"];

    public static FormContract Compile(string schema)
    {
        if (schema.Length > 1048576) Fail("schema.size_limit");
        try
        {
            using var document = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object) Fail("schema.object");
            if (Scripts.Any(key => Active(Get(root, key)))) Fail("schema.script");
            var version = Number(Get(root, "flowzer"), "contractVersion") ?? 1;
            if (version is not (1 or 2) || decimal.Truncate(version) != version) Fail("schema.version");
            var profile = version == 2 ? FormContract.ProfileV2 : FormContract.ProfileV1;
            List<FormField> fields = [];
            HashSet<string> ignored = new(StringComparer.Ordinal);
            Visit(Get(root, "components"), fields, ignored, [], readOnly: false, version: (int)version);
            var keys = fields.Select(field => field.Key).Concat(ignored).ToArray();
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) Fail("schema.duplicate_key");
            foreach (var field in fields)
                foreach (var condition in field.Conditions)
                    if (!fields.Any(other => other.Key == Text(condition, "when"))) Fail("condition.unknown_field");
                    else if (fields.Any(other => other.Key == Text(condition, "when")
                                                 && (NamedFormCalculations.IsCalculated(other)
                                                     || other.Type == "flowzerSubject")))
                        Fail("condition.unsupported_source");
            ValidateConditionGraph(fields);
            NamedFormCalculations.ValidateContract(fields);
            var rules = Get(Get(root, "flowzer"), "rules");
            ValidateRules(rules, fields);
            return new FormContract(profile, fields, ignored, rules);
        }
        catch (JsonException) { throw new InvalidOperationException("Unsupported form contract: schema.json."); }
    }

    private static void Visit(JsonElement components, List<FormField> fields, HashSet<string> ignored,
        IReadOnlyList<JsonElement> conditions, bool readOnly, int version)
    {
        if (components.ValueKind == JsonValueKind.Undefined) return;
        if (components.ValueKind != JsonValueKind.Array) Fail("schema.components");
        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object) Fail("schema.component");
            if (fields.Count + ignored.Count >= 500) Fail("schema.field_limit");
            if (Scripts.Any(key => Active(Get(component, key)))) Fail("schema.script");
            foreach (var flag in new[] { "input", "disabled", "multiple" }) ValidateBoolean(component, flag);
            var conditional = Get(component, "conditional");
            if (Active(Get(conditional, "json"))) Fail("condition.script");
            var when = Text(conditional, "when");
            IReadOnlyList<JsonElement> visibleWhen = conditions;
            if (when.Length > 0)
            {
                if (!SafeKey(when) || Get(conditional, "show").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || Get(conditional, "eq").ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Undefined) Fail("condition.unsupported");
                visibleWhen = [.. conditions, conditional];
            }
            var isReadOnly = readOnly || True(component, "disabled") || Text(Get(component, "flowzer"), "access") == "context";
            var type = Text(component, "type");
            if (LayoutTypes.Contains(type))
            {
                VisitLayout(component, fields, ignored, visibleWhen, isReadOnly, version);
                continue;
            }
            if (type is "button" or "content" or "htmlelement")
            {
                var ignoredKey = Text(component, "key");
                if (ignoredKey.Length > 0 && !ignored.Add(ignoredKey)) Fail("schema.duplicate_key");
                continue;
            }
            if (!FieldTypes.Contains(type)) Fail("schema.field_type");
            if (type == "flowzerSubject" && version != 2) Fail("schema.version");
            var key = Text(component, "key");
            if (!SafeKey(key)) Fail("schema.key");
            if (False(component, "input")) { if (!ignored.Add(key)) Fail("schema.duplicate_key"); continue; }
            var subjectSelection = ValidateField(component, type);
            fields.Add(new FormField(key, type, component, isReadOnly, visibleWhen, subjectSelection));
        }
    }

    private static void VisitLayout(JsonElement layout, List<FormField> fields, HashSet<string> ignored,
        IReadOnlyList<JsonElement> conditions, bool readOnly, int version)
    {
        Visit(Get(layout, "components"), fields, ignored, conditions, readOnly, version);
        foreach (var name in new[] { "columns", "rows" })
        {
            var children = Get(layout, name);
            if (children.ValueKind == JsonValueKind.Undefined) continue;
            if (children.ValueKind != JsonValueKind.Array) Fail("schema.layout");
            foreach (var child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object) VisitLayout(child, fields, ignored, conditions, readOnly, version);
                else if (child.ValueKind == JsonValueKind.Array)
                    foreach (var cell in child.EnumerateArray()) VisitLayout(cell, fields, ignored, conditions, readOnly, version);
                else Fail("schema.layout");
            }
        }
    }

    private static DirectorySubjectSelectionPolicy? ValidateField(JsonElement component, string type)
    {
        var validate = Get(component, "validate");
        if (validate.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) Fail("validation.object");
        if (validate.ValueKind == JsonValueKind.Object && validate.EnumerateObject().Any(property => !ValidationKeys.Contains(property.Name) && Active(property.Value))) Fail("validation.unsupported");
        ValidateBoolean(validate, "required");
        foreach (var (minimum, maximum) in new[] { ("minLength", "maxLength"), ("min", "max"), ("minSelectedCount", "maxSelectedCount") })
        {
            var min = Number(validate, minimum);
            var max = Number(validate, maximum);
            if (min > max) Fail("validation.range");
            if (minimum != "min" && new[] { min, max }.Any(value => value.HasValue && (value < 0 || decimal.Truncate(value.Value) != value.Value))) Fail("validation.count");
        }
        if (type is not ("number" or "currency") && (Number(validate, "min").HasValue || Number(validate, "max").HasValue)) Fail("validation.numeric_type");
        // Feldfremde Regeln nicht scheinbar akzeptieren: Der Skalarprüfer würde sie
        // sonst nie auswerten. min=0 bei Anzahlen ist Form.io-Default ohne Einschränkung.
        if (type is "number" or "currency" or "checkbox" or "select" or "radio" or "hidden" or "flowzerSubject"
            && (Number(validate, "minLength") is > 0 || Number(validate, "maxLength").HasValue || Active(Get(validate, "pattern")))) Fail("validation.text_type");
        if (!True(component, "multiple") && (Number(validate, "minSelectedCount") is > 0 || Number(validate, "maxSelectedCount").HasValue)) Fail("validation.multiple_type");
        if (Active(Get(component, "inputMask"))) Fail("validation.input_mask");
        foreach (var options in new[] { Get(component, "datePicker"), Get(component, "timePicker"), Get(component, "widget") })
            foreach (var key in new[] { "minDate", "maxDate", "minTime", "maxTime" })
                if (Active(Get(options, key))) Fail("validation.date_bound");
        if (Active(Get(validate, "pattern")))
        {
            if (Get(validate, "pattern").ValueKind != JsonValueKind.String || Text(validate, "pattern").Length > 1024) Fail("validation.pattern_limit");
            try { _ = Pattern(Text(validate, "pattern")); }
            catch (ArgumentException) { Fail("validation.pattern"); }
            catch (NotSupportedException) { Fail("validation.pattern"); }
        }
        if (type is "select" or "radio")
        {
            if (type == "select" && Text(component, "dataSrc") is not ("" or "values")) Fail("selection.dynamic_source");
            var values = type == "radio" ? Get(component, "values") : Get(Get(component, "data"), "values");
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 500 || values.EnumerateArray().Any(item => Get(item, "value").ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))) Fail("selection.values");
        }
        var access = Text(Get(component, "flowzer"), "access");
        if (access is not ("" or "input" or "context")) Fail("field.access");
        var selection = Get(Get(component, "flowzer"), "subjectSelection");
        if (type != "flowzerSubject")
        {
            if (Active(selection)) Fail("selection.subject_type");
            return null;
        }

        return CompileSubjectSelection(selection);
    }

    private static DirectorySubjectSelectionPolicy CompileSubjectSelection(JsonElement selection)
    {
        if (selection.ValueKind == JsonValueKind.Undefined)
        {
            return new DirectorySubjectSelectionPolicy { AllowUsers = true, ActiveOnly = true };
        }

        if (selection.ValueKind != JsonValueKind.Object) Fail("selection.policy_object");
        var allowedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "allowUsers", "allowGroups", "activeOnly", "allowedUserIds",
            "userMemberOfGroupIds", "allowedGroupIds", "includeSubgroups"
        };
        if (selection.EnumerateObject().Any(property => !allowedKeys.Contains(property.Name)))
            Fail("selection.policy_unknown");
        foreach (var key in new[] { "allowUsers", "allowGroups", "activeOnly", "includeSubgroups" })
            ValidateBoolean(selection, key);

        var allowUsers = Get(selection, "allowUsers").ValueKind == JsonValueKind.Undefined || True(selection, "allowUsers");
        var allowGroups = True(selection, "allowGroups");
        // Profil 2 bietet absichtlich keine Auswahl deaktivierter Identitaeten. Historische
        // Referenzen bleiben aufloesbar, duerfen aber nicht neu in Prozesse geschrieben werden.
        if (Get(selection, "activeOnly").ValueKind != JsonValueKind.Undefined && !True(selection, "activeOnly"))
            Fail("selection.inactive_unsupported");
        if (!allowUsers && !allowGroups) Fail("selection.empty_policy");

        var allowedUserIds = IdSet(selection, "allowedUserIds");
        var memberOfGroupIds = IdSet(selection, "userMemberOfGroupIds");
        var allowedGroupIds = IdSet(selection, "allowedGroupIds");
        if (!allowUsers && (allowedUserIds is not null || memberOfGroupIds is not null))
            Fail("selection.user_filter_without_users");
        if (!allowGroups && allowedGroupIds is not null)
            Fail("selection.group_filter_without_groups");

        return new DirectorySubjectSelectionPolicy
        {
            AllowUsers = allowUsers,
            AllowGroups = allowGroups,
            ActiveOnly = true,
            AllowedUserIds = allowedUserIds,
            UserMemberOfGroupIds = memberOfGroupIds,
            AllowedGroupIds = allowedGroupIds,
            IncludeSubgroups = True(selection, "includeSubgroups")
        };
    }

    private static IReadOnlySet<Guid>? IdSet(JsonElement parent, string key)
    {
        var value = Get(parent, key);
        if (value.ValueKind == JsonValueKind.Undefined) return null;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 500) Fail("selection.ids");
        HashSet<Guid> ids = [];
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String
                || !Guid.TryParse(entry.GetString(), out var id)
                || id == Guid.Empty
                || !ids.Add(id))
            {
                Fail("selection.ids");
            }
        }
        // Form.io legt konfigurierte Listen standardmaessig als leere Arrays an. Leer bedeutet
        // deshalb wie "nicht gesetzt" und darf die Auswahl nicht versehentlich auf null Eintraege sperren.
        return ids.Count == 0 ? null : ids;
    }

    private static void ValidateBoolean(JsonElement parent, string key)
    {
        if (Get(parent, key).ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False)) Fail("schema.boolean");
    }

    private static void ValidateConditionGraph(IReadOnlyList<FormField> fields)
    {
        var byKey = fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        HashSet<string> active = [], complete = [];
        foreach (var field in fields) VisitDependency(field.Key);
        return;

        void VisitDependency(string key)
        {
            if (complete.Contains(key)) return;
            if (!active.Add(key)) Fail("condition.cycle");
            foreach (var condition in byKey[key].Conditions) VisitDependency(Text(condition, "when"));
            active.Remove(key);
            complete.Add(key);
        }
    }

    private static void ValidateRules(JsonElement rules, IReadOnlyList<FormField> fields)
    {
        if (rules.ValueKind == JsonValueKind.Undefined) return;
        if (rules.ValueKind != JsonValueKind.Array || rules.GetArrayLength() > 100) Fail("rules.unsupported");
        foreach (var rule in rules.EnumerateArray())
        {
            if (Text(rule, "kind") != "dateOrder") Fail("rule.unsupported");
            ValidateBoolean(rule, "allowEqual");
            foreach (var key in new[] { Text(rule, "start"), Text(rule, "end") })
                if (!fields.Any(field => field.Key == key && field.Type == "datetime")) Fail("rule.date_field");
        }
    }

    internal static Regex Pattern(string pattern) => new($"\\A(?:{pattern})\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
    private static void Fail(string code) => throw new InvalidOperationException($"Unsupported form contract: {code}.");
}
