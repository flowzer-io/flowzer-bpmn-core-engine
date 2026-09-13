using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StorageSystem;

namespace WebApiEngine.Forms;

/// <summary>
/// Bindet ausschliesslich konkrete Formular- und Abschnittsversionen und erzeugt daraus
/// einen eigenstaendigen, serverseitig validierten Formularsnapshot. Die Laufzeit benoetigt
/// deshalb weder Bibliothek noch eine Aufloesung auf "latest". Abschnitte bleiben nur als
/// Abwaertskompatibilitaet fuer bereits gespeicherte Entwuerfe erhalten; neue Referenzen
/// verwenden Formulare als Komponenten.
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
        // Kompatibler Einstieg fuer bestehende Abschnittstests und Altcode. Neue
        // Veroeffentlichungspfade muessen den FormStorage und die Eigentuemerlkennung nennen.
        return await ExpandAsync(null, storage, Guid.Empty, formData);
    }

    public static async Task<string> ExpandAsync(
        IFormStorage? formStorage,
        IFormSectionStorage sectionStorage,
        Guid ownerFormId,
        string formData)
    {
        ArgumentNullException.ThrowIfNull(sectionStorage);
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
        flowzer?.Remove("boundForms");
        List<JsonObject> sectionBindings = [];
        List<JsonObject> formBindings = [];
        if (root["components"] is JsonArray components)
            await ExpandArrayAsync(
                components,
                formStorage,
                sectionStorage,
                ownerFormId,
                ownerFormId == Guid.Empty
                    ? new HashSet<Guid>()
                    : new HashSet<Guid> { ownerFormId },
                sectionBindings,
                formBindings);

        if (sectionBindings.Count > 0 || formBindings.Count > 0)
        {
            flowzer ??= new JsonObject();
            root["flowzer"] = flowzer;
            if (sectionBindings.Count > 0)
                flowzer["boundSections"] = new JsonArray(sectionBindings.Select(binding => (JsonNode)binding).ToArray());
            if (formBindings.Count > 0)
                flowzer["boundForms"] = new JsonArray(formBindings.Select(binding => (JsonNode)binding).ToArray());
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
        IFormStorage? formStorage,
        IFormSectionStorage sectionStorage,
        Guid ownerFormId,
        IReadOnlySet<Guid> formPath,
        List<JsonObject> sectionBindings,
        List<JsonObject> formBindings)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            // Form.io bildet Tabellenzeilen als verschachtelte Arrays ab. Sie werden im
            // selben Durchlauf behandelt, ohne den restlichen Baum anschließend erneut
            // zu traversieren (bei tiefen Layouts wäre das sonst exponentiell).
            if (nodes[index] is JsonArray nestedArray)
            {
                await ExpandArrayAsync(
                    nestedArray, formStorage, sectionStorage, ownerFormId, formPath,
                    sectionBindings, formBindings);
                continue;
            }
            if (nodes[index] is not JsonObject component) continue;
            if (String(component, "type") == "flowzerSection")
            {
                var replacement = await ResolveSectionAsync(component, sectionStorage, sectionBindings);
                nodes.RemoveAt(index);
                foreach (var child in replacement)
                    nodes.Insert(index++, child?.DeepClone());
                index--;
                continue;
            }

            if (String(component, "type") == "flowzerForm")
            {
                if (formStorage is null)
                    throw new FormContractException("form.reference_not_supported");
                var replacement = await ResolveFormAsync(
                    component,
                    formStorage,
                    sectionStorage,
                    ownerFormId,
                    formPath,
                    sectionBindings,
                    formBindings);
                nodes.RemoveAt(index);
                foreach (var child in replacement)
                    nodes.Insert(index++, child?.DeepClone());
                index--;
                continue;
            }

            foreach (var property in new[] { "components", "columns", "rows" })
                if (component[property] is JsonArray children)
                    await ExpandArrayAsync(
                        children, formStorage, sectionStorage, ownerFormId, formPath,
                        sectionBindings, formBindings);
        }
    }

    private static async Task<JsonArray> ResolveSectionAsync(
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

    private static async Task<JsonArray> ResolveFormAsync(
        JsonObject reference,
        IFormStorage formStorage,
        IFormSectionStorage sectionStorage,
        Guid ownerFormId,
        IReadOnlySet<Guid> formPath,
        List<JsonObject> sectionBindings,
        List<JsonObject> formBindings)
    {
        if (reference.Any(property =>
                ForbiddenReferenceProperties.Contains(property.Key) && IsActive(property.Value)))
            throw new FormContractException("form.reference_property");

        var referenceKey = String(reference, "key");
        if (!FormJson.SafeKey(referenceKey))
            throw new FormContractException("form.reference_key");
        if (!Guid.TryParse(String(reference, "formId"), out var formId) || formId == Guid.Empty)
            throw new FormContractException("form.reference_id");
        if (formId == ownerFormId || formPath.Contains(formId))
            throw new FormContractException("form.reference_cycle");
        var versionText = String(reference, "version");
        if (!TryParseVersion(versionText, out var version))
            throw new FormContractException("form.reference_version");

        Form form;
        try
        {
            form = (await formStorage.GetForms(formId))
                .SingleOrDefault(candidate => candidate.Version == version)
                ?? throw new FileNotFoundException();
        }
        catch (FileNotFoundException)
        {
            throw new FormContractException("form.version_not_found");
        }
        if (form.FormId != formId || form.Version != version)
            throw new FormContractException("form.version_mismatch");

        JsonObject componentRoot;
        try
        {
            componentRoot = JsonNode.Parse(
                form.FormData,
                documentOptions: new JsonDocumentOptions { MaxDepth = 32 })?.AsObject()
                ?? throw new FormContractException("schema.object");
        }
        catch (JsonException)
        {
            throw new FormContractException("schema.json");
        }

        var components = componentRoot["components"] as JsonArray
                         ?? throw new FormContractException("schema.components");
        var nextPath = new HashSet<Guid>(formPath) { formId };
        await ExpandArrayAsync(
            components,
            formStorage,
            sectionStorage,
            ownerFormId,
            nextPath,
            sectionBindings,
            formBindings);

        formBindings.Add(new JsonObject
        {
            ["referenceKey"] = referenceKey,
            ["formId"] = form.FormId,
            ["formVersionId"] = form.Id,
            ["version"] = form.Version.ToString(),
            ["contentSha256"] = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(form.FormData))).ToLowerInvariant()
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
        // Form.io schreibt z. B. conditional={show:null,when:null,eq:""} als
        // inaktiven Standard. Entscheidend sind wirksame Werte, nicht die bloße Anzahl Keys.
        JsonObject value => value.Any(property => IsActive(property.Value)),
        _ => true
    };
}
