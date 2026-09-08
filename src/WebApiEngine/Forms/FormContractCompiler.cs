using System.Diagnostics.CodeAnalysis;
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
            if (version is not (1 or 2 or 3) || decimal.Truncate(version) != version) Fail("schema.version");
            var profile = version switch
            {
                2 => FormContract.ProfileV2,
                3 => FormContract.ProfileV3,
                _ => FormContract.ProfileV1
            };
            List<FormField> fields = [];
            List<FormRepeatGroup> repeatGroups = [];
            HashSet<string> ignored = new(StringComparer.Ordinal);
            Visit(Get(root, "components"), fields, repeatGroups, ignored, [], readOnly: false, version: (int)version);
            var keys = fields.Select(field => field.Key)
                .Concat(repeatGroups.Select(group => group.Key))
                .Concat(ignored)
                .ToArray();
            if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) Fail("schema.duplicate_key");
            ValidateConditionSources(fields, fields);
            foreach (var group in repeatGroups)
            {
                ValidateConditionSources(group.Fields, group.Fields);
                ValidateConditionSources(group.Conditions, fields);
            }
            ValidateConditionGraph(fields);
            foreach (var group in repeatGroups) ValidateConditionGraph(group.Fields);
            NamedFormCalculations.ValidateContract(fields);
            var rules = Get(Get(root, "flowzer"), "rules");
            ValidateRules(rules, fields);
            return new FormContract(profile, fields, ignored, rules) { RepeatGroups = repeatGroups };
        }
        catch (JsonException) { throw new FormContractException("schema.json"); }
    }

    private static void Visit(JsonElement components, List<FormField> fields,
        List<FormRepeatGroup> repeatGroups, HashSet<string> ignored,
        IReadOnlyList<JsonElement> conditions, bool readOnly, int version)
    {
        if (components.ValueKind == JsonValueKind.Undefined) return;
        if (components.ValueKind != JsonValueKind.Array) Fail("schema.components");
        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object) Fail("schema.component");
            if (fields.Count + repeatGroups.Count + repeatGroups.Sum(group => group.Fields.Count) + ignored.Count >= 500)
                Fail("schema.field_limit");
            if (Scripts.Any(key => Active(Get(component, key)))) Fail("schema.script");
            ValidateHelpText(component, version);
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
            if (type != "datagrid" && Active(Get(Get(component, "flowzer"), "repeat")))
                Fail("repeat.unexpected");
            if (LayoutTypes.Contains(type))
            {
                VisitLayout(component, fields, repeatGroups, ignored, visibleWhen, isReadOnly, version);
                continue;
            }
            if (type == "datagrid")
            {
                if (version != 3) Fail("schema.version");
                repeatGroups.Add(CompileRepeatGroup(component, visibleWhen, isReadOnly, version));
                if (fields.Count + repeatGroups.Count + repeatGroups.Sum(group => group.Fields.Count) + ignored.Count > 500)
                    Fail("schema.field_limit");
                continue;
            }
            if (type is "button" or "content" or "htmlelement")
            {
                var ignoredKey = Text(component, "key");
                if (ignoredKey.Length > 0 && !ignored.Add(ignoredKey)) Fail("schema.duplicate_key");
                continue;
            }
            if (!FieldTypes.Contains(type)) Fail("schema.field_type");
            if (type == "flowzerSubject" && version < 2) Fail("schema.version");
            var key = Text(component, "key");
            if (!SafeKey(key)) Fail("schema.key");
            if (False(component, "input")) { if (!ignored.Add(key)) Fail("schema.duplicate_key"); continue; }
            var subjectSelection = ValidateField(component, type);
            fields.Add(new FormField(key, type, component, isReadOnly, visibleWhen, subjectSelection));
        }
    }

    private static void VisitLayout(JsonElement layout, List<FormField> fields,
        List<FormRepeatGroup> repeatGroups, HashSet<string> ignored,
        IReadOnlyList<JsonElement> conditions, bool readOnly, int version)
    {
        Visit(Get(layout, "components"), fields, repeatGroups, ignored, conditions, readOnly, version);
        foreach (var name in new[] { "columns", "rows" })
        {
            var children = Get(layout, name);
            if (children.ValueKind == JsonValueKind.Undefined) continue;
            if (children.ValueKind != JsonValueKind.Array) Fail("schema.layout");
            foreach (var child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object) VisitLayout(child, fields, repeatGroups, ignored, conditions, readOnly, version);
                else if (child.ValueKind == JsonValueKind.Array)
                    foreach (var cell in child.EnumerateArray()) VisitLayout(cell, fields, repeatGroups, ignored, conditions, readOnly, version);
                else Fail("schema.layout");
            }
        }
    }

    private static FormRepeatGroup CompileRepeatGroup(
        JsonElement component,
        IReadOnlyList<JsonElement> conditions,
        bool readOnly,
        int version)
    {
        var key = Text(component, "key");
        if (!SafeKey(key)) Fail("schema.key");
        if (False(component, "input")) Fail("repeat.input");
        if (True(component, "multiple")) Fail("repeat.multiple");

        var flowzer = Get(component, "flowzer");
        if (flowzer.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
            Fail("repeat.policy_object");
        if (flowzer.ValueKind == JsonValueKind.Object
            && flowzer.EnumerateObject().Any(property => property.Name is not ("repeat" or "helpText" or "access")
                                                          && Active(property.Value)))
            Fail("repeat.policy_unknown");
        var accessValue = Get(flowzer, "access");
        if (accessValue.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String))
            Fail("field.access");
        var access = Text(flowzer, "access");
        if (access is not ("" or "input" or "context")) Fail("field.access");
        var repeat = Get(flowzer, "repeat");
        if (repeat.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
            Fail("repeat.policy_object");
        if (repeat.ValueKind == JsonValueKind.Object
            && repeat.EnumerateObject().Any(property => property.Name is not ("minItems" or "maxItems")))
            Fail("repeat.policy_unknown");
        var minimum = Number(repeat, "minItems") ?? 0;
        var maximum = Number(repeat, "maxItems") ?? 20;
        if (minimum < 0 || maximum < 1 || maximum > 50
            || minimum > maximum
            || decimal.Truncate(minimum) != minimum
            || decimal.Truncate(maximum) != maximum)
            Fail("repeat.range");

        // Form.io stellt fuer Datagrids seine generischen Laengenfelder dar. Der Builder
        // bindet sie beim Speichern an die Flowzer-Policy; abweichende Doppelangaben
        // duerfen in der Oberflaeche keine andere Regel vortaeuschen als der Server nutzt.
        var validate = Get(component, "validate");
        if (validate.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
            Fail("validation.object");
        if (validate.ValueKind == JsonValueKind.Object
            && validate.EnumerateObject().Any(property => property.Name is not ("required" or "minLength" or "maxLength" or "customMessage")
                                                          && Active(property.Value)))
            Fail("validation.unsupported");
        ValidateBoolean(validate, "required");
        var formioMinimum = Number(validate, "minLength");
        var formioMaximum = Number(validate, "maxLength");
        if (formioMinimum.HasValue && formioMinimum != minimum
            || formioMaximum is > 0 && formioMaximum != maximum
            || True(validate, "required") && minimum < 1)
            Fail("repeat.formio_mismatch");

        List<FormField> fields = [];
        var children = Get(component, "components");
        if (children.ValueKind != JsonValueKind.Array) Fail("repeat.components");
        foreach (var child in children.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.Object) Fail("schema.component");
            if (fields.Count >= 100) Fail("repeat.field_limit");
            if (Scripts.Any(script => Active(Get(child, script)))) Fail("schema.script");
            ValidateHelpText(child, version);
            foreach (var flag in new[] { "input", "disabled", "multiple" }) ValidateBoolean(child, flag);
            var type = Text(child, "type");
            if (type == "datagrid") Fail("repeat.nested");
            if (!FieldTypes.Contains(type) || type == "flowzerSubject") Fail("repeat.field_type");
            if (False(child, "input")) Fail("repeat.input");
            if (True(child, "multiple")) Fail("repeat.multiple_field");
            if (True(child, "disabled") || Text(Get(child, "flowzer"), "access") == "context")
                Fail("repeat.read_only_field");
            if (Active(Get(Get(child, "flowzer"), "calculation"))) Fail("repeat.calculation");
            var childKey = Text(child, "key");
            if (!SafeKey(childKey)) Fail("schema.key");
            var childConditions = ReadConditions(child, []);
            var selection = ValidateField(child, type);
            fields.Add(new FormField(childKey, type, child, false, childConditions, selection));
        }
        if (fields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            Fail("schema.duplicate_key");
        return new FormRepeatGroup(key, component, readOnly, conditions, fields, (int)minimum, (int)maximum);
    }

    private static IReadOnlyList<JsonElement> ReadConditions(
        JsonElement component,
        IReadOnlyList<JsonElement> inherited)
    {
        var conditional = Get(component, "conditional");
        if (Active(Get(conditional, "json"))) Fail("condition.script");
        var when = Text(conditional, "when");
        if (when.Length == 0) return inherited;
        if (!SafeKey(when)
            || Get(conditional, "show").ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || Get(conditional, "eq").ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Undefined)
            Fail("condition.unsupported");
        return [.. inherited, conditional];
    }

    private static void ValidateHelpText(JsonElement component, int version)
    {
        if (version != 3) return;
        foreach (var value in new[] { Get(component, "description"), Get(Get(component, "flowzer"), "helpText") })
        {
            if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
            if (value.ValueKind != JsonValueKind.String) Fail("help.type");
            var text = value.GetString()!;
            if (text.Length > 2000) Fail("help.length");
            if (text.Contains('<') || text.Contains('>')
                || text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                Fail("help.plain_text");
        }
    }

    private static void ValidateConditionSources(
        IReadOnlyList<FormField> conditionedFields,
        IReadOnlyList<FormField> availableFields)
    {
        foreach (var field in conditionedFields)
            ValidateConditionSources(field.Conditions, availableFields);
    }

    private static void ValidateConditionSources(
        IReadOnlyList<JsonElement> conditions,
        IReadOnlyList<FormField> availableFields)
    {
        foreach (var condition in conditions)
        {
            var key = Text(condition, "when");
            var source = availableFields.SingleOrDefault(field => field.Key == key);
            if (source is null) Fail("condition.unknown_field");
            if (NamedFormCalculations.IsCalculated(source) || source.Type == "flowzerSubject")
                Fail("condition.unsupported_source");
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
    [DoesNotReturn]
    private static void Fail(string code) => throw new FormContractException(code);
}
