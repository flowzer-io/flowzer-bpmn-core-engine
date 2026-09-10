using System.Dynamic;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace WebApiEngine.Forms;

/// <summary>
/// Minimiert vorhandene Aufgabenwerte anhand des gespeicherten Formularschemas.
/// Keine Ausführung von Formular-JavaScript, keine Objekt-/Scope-Freigabe durch einen
/// bloßen Feldnamen. Dies ist eine Leseprojektion, noch keine Submission-Validierung.
/// </summary>
public static class FormContextProjection
{
    private static readonly HashSet<string> ScalarTypes =
        ["textfield", "textarea", "email", "url", "phoneNumber", "number", "currency", "checkbox", "radio", "select", "datetime", "day", "time", "hidden"];
    private static readonly HashSet<string> LayoutTypes = ["panel", "fieldset", "columns", "table", "tabs", "well"];

    public static ExpandoObject Project(string? schema, ExpandoObject? source)
    {
        ExpandoObject result = new();
        if (schema is null || source is null) return result;
        try
        {
            using var document = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("components", out var components))
            {
                ProjectComponents(components, source, result);
            }
        }
        catch (JsonException)
        {
            // Ein beschädigtes oder unverständlich tiefes Schema ist nie eine Freigabe.
            return new ExpandoObject();
        }
        return result;
    }

    private static void ProjectComponents(JsonElement components, object source, ExpandoObject result)
    {
        if (components.ValueKind != JsonValueKind.Array) return;
        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object) continue;
            var type = Text(component, "type");
            if (LayoutTypes.Contains(type))
            {
                ProjectLayout(component, source, result);
                continue;
            }

            var key = Text(component, "key");
            if (!IsSafePath(key) || IsFalse(component, "input")) continue;
            if (!TryReadPath(source, key, out var value)) continue;

            if (type == "container" && component.TryGetProperty("components", out var nested))
            {
                ExpandoObject selected = new();
                if (value is not null) ProjectComponents(nested, value, selected);
                WritePath(result, key, selected);
            }
            else if (ScalarTypes.Contains(type) && TryScalar(value, out var scalar))
            {
                WritePath(result, key, scalar);
            }
            else if (ScalarTypes.Contains(type) && IsTrue(component, "multiple") && TryScalarArray(value, out var array))
            {
                WritePath(result, key, array);
            }
        }
    }

    private static void ProjectLayout(JsonElement layout, object source, ExpandoObject result)
    {
        if (layout.TryGetProperty("components", out var components)) ProjectComponents(components, source, result);
        foreach (var property in new[] { "columns", "rows" })
        {
            if (!layout.TryGetProperty(property, out var children) || children.ValueKind != JsonValueKind.Array) continue;
            foreach (var child in children.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Object) ProjectLayout(child, source, result);
                if (child.ValueKind == JsonValueKind.Array)
                    foreach (var cell in child.EnumerateArray())
                        if (cell.ValueKind == JsonValueKind.Object) ProjectLayout(cell, source, result);
            }
        }
    }

    private static bool TryScalar(object? value, out object? scalar)
    {
        if (value is JValue jValue) value = jValue.Value;
        if (value is JsonElement json && json.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)
        {
            scalar = json.Clone();
            return true;
        }
        scalar = value;
        return value is null or string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal or DateTime or DateTimeOffset
            || value is double number && double.IsFinite(number)
            || value is float single && float.IsFinite(single);
    }

    private static bool TryScalarArray(object? value, out List<object?> result)
    {
        result = [];
        IEnumerable<object?>? values = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } json => json.EnumerateArray().Select(item => (object?)item),
            System.Collections.IEnumerable sequence when value is not string => sequence.Cast<object?>(),
            _ => null
        };
        if (values is null) return false;
        foreach (var item in values)
        {
            if (!TryScalar(item, out var scalar)) return false;
            result.Add(scalar);
        }
        return true;
    }

    private static bool TryReadPath(object source, string path, out object? value)
    {
        value = source;
        foreach (var part in path.Split('.'))
        {
            switch (value)
            {
                case IDictionary<string, object?> dictionary when dictionary.TryGetValue(part, out var item): value = item; break;
                case JsonElement { ValueKind: JsonValueKind.Object } json when json.TryGetProperty(part, out var item): value = item; break;
                case JObject json when json.TryGetValue(part, StringComparison.Ordinal, out var item): value = item; break;
                default: return false;
            }
        }
        return true;
    }

    private static void WritePath(ExpandoObject root, string path, object? value)
    {
        var parts = path.Split('.');
        var target = (IDictionary<string, object?>)root;
        foreach (var part in parts[..^1])
        {
            if (!target.TryGetValue(part, out var child) || child is not ExpandoObject)
                target[part] = child = new ExpandoObject();
            target = (IDictionary<string, object?>)child;
        }
        target[parts[^1]] = value;
    }

    private static bool IsSafePath(string path) => path.Length is > 0 and <= 512
        && path.Split('.').All(part => part.Length is > 0 and <= 128
            && part is not "__proto__" and not "constructor" and not "prototype");
    private static string Text(JsonElement value, string key) =>
        value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : "";
    private static bool IsTrue(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.True;
    private static bool IsFalse(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.False;
}
