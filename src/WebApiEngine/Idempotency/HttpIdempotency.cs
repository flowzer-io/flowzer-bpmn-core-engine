using System.Globalization;
using System.Numerics;
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
            case JsonValueKind.Number:
                // JsonElement bewahrt die lexikalische Schreibweise. Für Idempotenz ist
                // jedoch der exakte Dezimalwert maßgeblich: 1, 1.0 und 1e0 sind gleich.
                // Die Normalisierung arbeitet auf Ziffern/BigInteger und vermeidet damit
                // jede Rundung über double oder decimal.
                writer.WriteRawValue(NormalizeJsonNumber(value.GetRawText()));
                break;
            default: value.WriteTo(writer); break;
        }
    }

    private static string NormalizeJsonNumber(string rawNumber)
    {
        var exponentIndex = rawNumber.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? rawNumber : rawNumber[..exponentIndex];
        var exponent = exponentIndex < 0
            ? BigInteger.Zero
            : BigInteger.Parse(rawNumber[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture);
        var negative = mantissa[0] == '-';
        var unsignedMantissa = negative ? mantissa[1..] : mantissa;
        var decimalPoint = unsignedMantissa.IndexOf('.');
        var fractionLength = decimalPoint < 0 ? 0 : unsignedMantissa.Length - decimalPoint - 1;
        var digits = decimalPoint < 0
            ? unsignedMantissa
            : string.Concat(unsignedMantissa.AsSpan(0, decimalPoint), unsignedMantissa.AsSpan(decimalPoint + 1));
        digits = digits.TrimStart('0');
        if (digits.Length == 0) return "0";

        var trailingZeros = digits.Length - digits.TrimEnd('0').Length;
        var significantDigits = trailingZeros == 0 ? digits : digits[..^trailingZeros];
        var normalizedExponent = exponent - fractionLength + trailingZeros;
        return $"{(negative ? "-" : "")}{significantDigits}e{normalizedExponent.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record IdempotencyRequest(string ScopeHash, string RequestHash, string Operation);
public sealed class IdempotencyConflictException(string message) : Exception(message);
