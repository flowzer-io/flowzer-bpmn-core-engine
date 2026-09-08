using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebApiEngine.Auth;

namespace WebApiEngine.Idempotency;

/// <summary>Validiert den HTTP-Schlüssel und bindet ihn an Akteur, Operation, Ziel und Inhalt.</summary>
public static class HttpIdempotency
{
    public const string HeaderName = "Idempotency-Key";
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public static IdempotencyRequest? Create(HttpRequest request, CurrentUserContext actor,
        string operation, string resource, object? payload)
    {
        if (!request.Headers.TryGetValue(HeaderName, out var values)) return null;
        if (values.Count != 1) throw new BadHttpRequestException("Exactly one Idempotency-Key header is required.");
        var key = values[0] ?? "";
        if (key.Length is < 1 or > 200 || key.Any(character => character is < '!' or > '~'))
            throw new BadHttpRequestException("Idempotency-Key must contain 1 to 200 visible ASCII characters.");

        actor.RequireResolvedUserId("using idempotency keys");
        var identity = actor.Identity
            ?? throw new UnauthorizedAccessException("A stable issuer and subject are required for idempotent HTTP requests.");
        var subject = $"{identity.Issuer}\0{identity.Subject}";
        var scope = Hash($"{operation}\0{resource}\0{subject}\0{key}");
        return new IdempotencyRequest(scope, Hash(CanonicalJson(payload)), operation);
    }

    private static string CanonicalJson(object? payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, document.RootElement);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray();
                break;
            default: value.WriteTo(writer); break;
        }
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record IdempotencyRequest(string ScopeHash, string RequestHash, string Operation);
public sealed class IdempotencyConflictException(string message) : Exception(message);
