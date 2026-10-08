using System.Text.Json;

namespace WebApiEngine.FormEmbedding;

/// <summary>Zusätzliche geschlossene Asset-Grenze nur für den isolierten Embed-Renderer.</summary>
public static class FormEmbeddingSchemaSupport
{
    /// <summary>Erweiterte Editoren und Kalenderplugins benötigen noch nicht gebündelte Assets.</summary>
    public static bool IsSupported(string schema)
    {
        using var document = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 32 });
        return Check(document.RootElement);
    }

    private static bool Check(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array) return node.EnumerateArray().All(Check);
        if (node.ValueKind != JsonValueKind.Object) return true;
        var type = node.TryGetProperty("type", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (type == "textarea" && (Active(node, "editor") || Active(node, "wysiwyg"))) return false;
        if (type == "datetime" && Active(node, "shortcutButtons")) return false;
        return node.EnumerateObject().All(property => Check(property.Value));
    }

    private static bool Active(JsonElement node, string key) => node.TryGetProperty(key, out var value) && value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        _ => true
    };
}
