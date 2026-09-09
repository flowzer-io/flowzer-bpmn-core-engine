using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StorageSystem;

namespace WebApiEngine.Forms;

/// <summary>
/// Bindet ausschliesslich konkrete Abschnittsversionen und erzeugt daraus einen
/// eigenstaendigen, serverseitig validierten Formularsnapshot. Die Laufzeit benoetigt
/// deshalb weder Abschnittsbibliothek noch eine Aufloesung auf "latest".
/// </summary>
public static class FormSectionBindingExpander
{
    private static readonly HashSet<string> ForbiddenReferenceProperties = new(StringComparer.Ordinal)
    {
        "calculateValue", "customDefaultValue", "customConditional", "logic", "conditional",
        "components", "columns", "rows", "data", "dataSrc", "url", "refreshOn", "event",
        "action", "flowzer"
    };

    public static async Task<string> ExpandAsync(IFormSectionStorage storage, string formData)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(formData);

        JsonObject root;
        try
        {
            root = JsonNode.Parse(
                formData,
                documentOptions: new JsonDocumentOptions { MaxDepth = 32 })?.AsObject()
                ?? throw new FormContractException("schema.object");
        }
        catch (JsonException)
        {
            throw new FormContractException("schema.json");
        }
        catch (InvalidOperationException exception) when (exception is not FormContractException)
        {
            throw new FormContractException("schema.object");
        }

        var flowzer = ExistingTrustedFlowzerObject(root);
        flowzer?.Remove("boundSections");
        List<JsonObject> bindings = [];
        if (root["components"] is JsonArray components)
            await ExpandArrayAsync(components, storage, bindings);

        if (bindings.Count > 0)
        {
            flowzer ??= new JsonObject();
            root["flowzer"] = flowzer;
            flowzer["boundSections"] = new JsonArray(bindings.Select(binding => (JsonNode)binding).ToArray());
        }

        var expanded = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        _ = FormContractCompiler.Compile(expanded);
        return expanded;
    }

    private static JsonObject? ExistingTrustedFlowzerObject(JsonObject root)
    {
        if (root["flowzer"] is null) return null;
        return root["flowzer"] as JsonObject
               ?? throw new FormContractException("schema.flowzer_object");
    }

    private static async Task ExpandArrayAsync(
        JsonArray nodes,
        IFormSectionStorage storage,
        List<JsonObject> bindings)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            if (nodes[index] is not JsonObject component) continue;
            if (String(component, "type") == "flowzerSection")
            {
                var replacement = await ResolveAsync(component, storage, bindings);
                nodes.RemoveAt(index);
                foreach (var child in replacement)
                    nodes.Insert(index++, child?.DeepClone());
                index--;
                continue;
            }

            foreach (var property in new[] { "components", "columns", "rows" })
                if (component[property] is JsonArray children)
                    await ExpandNestedAsync(children, storage, bindings);
        }
    }

    private static async Task ExpandNestedAsync(
        JsonArray nodes,
        IFormSectionStorage storage,
        List<JsonObject> bindings)
    {
        await ExpandArrayAsync(nodes, storage, bindings);
        foreach (var node in nodes)
        {
            if (node is JsonArray array)
                await ExpandNestedAsync(array, storage, bindings);
            else if (node is JsonObject container)
                foreach (var property in new[] { "components", "columns", "rows" })
                    if (container[property] is JsonArray children)
                        await ExpandNestedAsync(children, storage, bindings);
        }
    }

    private static async Task<JsonArray> ResolveAsync(
        JsonObject reference,
        IFormSectionStorage storage,
        List<JsonObject> bindings)
    {
        // Form.io ergänzt je Version harmlose Darstellungs-Defaults. Eine Allowlist würde
        // dadurch valide, servererzeugte Referenzen fragil machen. Verboten werden stattdessen
        // alle Eigenschaften, die Code, Daten, Bedingungen oder eigene Kinder einschleusen.
        if (reference.Any(property =>
                ForbiddenReferenceProperties.Contains(property.Key) && IsActive(property.Value)))
            throw new FormContractException("section.reference_property");

        var referenceKey = String(reference, "key");
        if (!FormJson.SafeKey(referenceKey))
            throw new FormContractException("section.reference_key");
        if (!Guid.TryParse(String(reference, "sectionId"), out var sectionId) || sectionId == Guid.Empty)
            throw new FormContractException("section.reference_id");
        var versionText = String(reference, "version");
        if (!TryParseVersion(versionText, out var version))
            throw new FormContractException("section.reference_version");

        FormSectionVersion section;
        try
        {
            section = await storage.GetVersion(sectionId, version!);
        }
        catch (FileNotFoundException)
        {
            throw new FormContractException("section.version_not_found");
        }
        if (section.SectionId != sectionId || section.Version != version)
            throw new FormContractException("section.version_mismatch");

        _ = FormSectionCompiler.Compile(section.SectionData);
        var sectionRoot = JsonNode.Parse(
            section.SectionData,
            documentOptions: new JsonDocumentOptions { MaxDepth = 32 })?.AsObject();
        var components = sectionRoot?["components"] as JsonArray
                         ?? throw new FormContractException("schema.components");
        bindings.Add(new JsonObject
        {
            ["referenceKey"] = referenceKey,
            ["sectionId"] = section.SectionId,
            ["sectionVersionId"] = section.Id,
            ["version"] = section.Version.ToString(),
            ["contentSha256"] = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(section.SectionData))).ToLowerInvariant()
        });
        return components;
    }

    private static bool TryParseVersion(string value, out Model.Version? version)
    {
        version = null;
        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || major < 0 || minor < 0
            || parts[0] != major.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || parts[1] != minor.ToString(System.Globalization.CultureInfo.InvariantCulture))
            return false;
        version = new Model.Version(major, minor);
        return true;
    }

    private static string String(JsonObject node, string property) =>
        node[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

    private static bool IsActive(JsonNode? node) => node switch
    {
        null => false,
        JsonValue value when value.TryGetValue<bool>(out var boolean) => boolean,
        JsonValue value when value.TryGetValue<string>(out var text) => !string.IsNullOrWhiteSpace(text),
        JsonArray array => array.Count > 0,
        JsonObject value => value.Count > 0,
        _ => true
    };
}
