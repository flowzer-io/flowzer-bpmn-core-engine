using System.Text.Json;
using Model;

namespace WebApiEngine.Forms;

/// <summary>Strikter Parser fuer den einzigen erlaubten Objektwert im Formularprofil 2.</summary>
internal static class DirectorySubjectValue
{
    public static bool TryParse(JsonElement value, out SubjectRef subject)
    {
        subject = default!;
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 2) return false;
        if (!value.TryGetProperty("kind", out var kindValue)
            || !value.TryGetProperty("id", out var idValue)
            || kindValue.ValueKind != JsonValueKind.String
            || idValue.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idValue.GetString(), out var id)
            || id == Guid.Empty)
        {
            return false;
        }

        var kind = kindValue.GetString() switch
        {
            "user" => DirectorySubjectKind.User,
            "group" => DirectorySubjectKind.Group,
            _ => (DirectorySubjectKind)(-1)
        };
        if (!Enum.IsDefined(kind)) return false;
        subject = new SubjectRef(kind, id);
        return true;
    }

    public static bool TryParse(object? value, out SubjectRef subject)
    {
        if (value is JsonElement json) return TryParse(json, out subject);
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(value), new JsonDocumentOptions { MaxDepth = 4 });
            return TryParse(document.RootElement, out subject);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            subject = default!;
            return false;
        }
    }

    public static Dictionary<string, object?> Normalize(SubjectRef subject) => new(StringComparer.Ordinal)
    {
        ["kind"] = subject.Kind == DirectorySubjectKind.User ? "user" : "group",
        ["id"] = subject.Id.ToString()
    };
}
