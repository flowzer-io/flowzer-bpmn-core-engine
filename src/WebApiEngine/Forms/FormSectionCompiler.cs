using System.Text.Json;
using static WebApiEngine.Forms.FormJson;

namespace WebApiEngine.Forms;

/// <summary>
/// Prueft wiederverwendbare Formularabschnitte gegen eine bewusst kleinere Teilmenge
/// des regulaeren Formularvertrags. Abschnittsreferenzen, Wiederholgruppen, Aktionen
/// und darstellender Fremdinhalt bleiben gesperrt, bis ihre Bindungssemantik explizit
/// versioniert ist. Die eigentliche Feldpruefung bleibt im gemeinsamen Compiler.
/// </summary>
public static class FormSectionCompiler
{
    private const int MaximumSchemaLength = 1048576;

    public static FormContract Compile(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Length > MaximumSchemaLength)
            return FormContractCompiler.Compile(schema);

        try
        {
            using var document = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                ValidateRootFeatures(root);
                InspectComponents(Get(root, "components"));
            }
        }
        catch (JsonException)
        {
            // Der gemeinsame Compiler besitzt den oeffentlichen, stabilen JSON-Fehlercode.
            return FormContractCompiler.Compile(schema);
        }

        return FormContractCompiler.Compile(schema);
    }

    private static void ValidateRootFeatures(JsonElement root)
    {
        var flowzer = Get(root, "flowzer");
        if (flowzer.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        if (flowzer.ValueKind != JsonValueKind.Object)
            throw new FormContractException("section.flowzer_object");

        if (flowzer.EnumerateObject().Any(property =>
                property.Name != "contractVersion" && Active(property.Value)))
            throw new FormContractException("section.root_feature_unsupported");
    }

    private static void InspectComponents(JsonElement components)
    {
        if (components.ValueKind != JsonValueKind.Array) return;
        foreach (var component in components.EnumerateArray())
            InspectNode(component);
    }

    private static void InspectNode(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray()) InspectNode(child);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object) return;

        var type = Text(node, "type");
        if (type == "flowzerSection") throw new FormContractException("section.nested");
        if (type == "datagrid") throw new FormContractException("section.repeat_group_unsupported");
        if (type is "button" or "content" or "htmlelement")
            throw new FormContractException("section.non_field_component");

        foreach (var name in new[] { "components", "columns", "rows" })
        {
            var children = Get(node, name);
            if (children.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                InspectNode(children);
        }
    }
}
