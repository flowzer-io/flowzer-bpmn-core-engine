using System.Text.Json;

namespace core_engine;

/// <summary>
/// Provideruebergreifendes, absichtlich kleines JSON-Schema-Profil fuer KI-Ergebnisse.
/// Externe Referenzen und kombinierende Konstrukte bleiben ausgeschlossen, damit dasselbe
/// Schema lokal, bei allen Providern und nach deren Antwort deterministisch pruefbar ist.
/// </summary>
public static class AiResultSchemaProfile
{
    public const string ProfileId = "flowzer.ai-result-schema/1";
    public const int MaximumSchemaLength = 50_000;
    public const int MaximumResultLength = 1_048_576;
    private const int MaximumDepth = 12;
    private const int MaximumProperties = 100;
    private const int MaximumEnumValues = 100;
    private const int MaximumCollectionItems = 10_000;

    private static readonly HashSet<string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "description",
        "properties",
        "required",
        "additionalProperties",
        "items",
        "enum",
        "minLength",
        "maxLength",
        "minimum",
        "maximum",
        "minItems",
        "maxItems"
    };

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal)
    {
        "object", "array", "string", "number", "integer", "boolean", "null"
    };

    /// <summary>Prueft, ob ein Schema vollstaendig innerhalb des portablen Profils liegt.</summary>
    public static void ValidateSchema(string schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson) || schemaJson.Length > MaximumSchemaLength)
            throw Failure("ai.result_schema.invalid_json", "$", "The AI result schema is not valid JSON.");

        try
        {
            using var document = JsonDocument.Parse(schemaJson, DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Failure("ai.result_schema.object_required", "$.type",
                    "The AI result schema must have root type object.");

            ValidateSchemaNode(document.RootElement, "$", 0);
            if (!HasType(document.RootElement, "object"))
                throw Failure("ai.result_schema.object_required", "$.type",
                    "The AI result schema must have root type object.");
        }
        catch (JsonException)
        {
            throw Failure("ai.result_schema.invalid_json", "$", "The AI result schema is not valid JSON.");
        }
    }

    /// <summary>
    /// Parst eine begrenzte Providerantwort und prueft sie erneut gegen das gespeicherte
    /// Schema. Der Rueckgabewert ist vom kurzlebigen Parserdokument unabhaengig.
    /// </summary>
    public static JsonElement ParseAndValidateResult(string schemaJson, string resultJson)
    {
        ValidateSchema(schemaJson);
        if (string.IsNullOrWhiteSpace(resultJson) || resultJson.Length > MaximumResultLength)
            throw Failure("ai.result.invalid_json", "$", "The AI result is not valid JSON.");

        try
        {
            using var schema = JsonDocument.Parse(schemaJson, DocumentOptions);
            using var result = JsonDocument.Parse(resultJson, DocumentOptions);
            ValidateResultNode(schema.RootElement, result.RootElement, "$", 0);
            return result.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Failure("ai.result.invalid_json", "$", "The AI result is not valid JSON.");
        }
    }

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    private static void ValidateSchemaNode(JsonElement schema, string path, int depth)
    {
        EnsureDepth(depth, schema: true, path);
        if (schema.ValueKind != JsonValueKind.Object)
            throw Failure("ai.result_schema.node_invalid", path, "Each schema node must be an object.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in schema.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw Failure("ai.result_schema.duplicate_keyword", ChildPath(path, property.Name),
                    "The AI result schema contains a duplicate keyword.");
            if (!SupportedKeywords.Contains(property.Name))
                throw Failure("ai.result_schema.keyword_unsupported", ChildPath(path, property.Name),
                    "The AI result schema contains an unsupported keyword.");
        }

        var type = RequiredType(schema, path);
        ValidateKeywordApplicability(schema, path, type);
        ValidateMetadata(schema, path);
        ValidateEnum(schema, path, type);
        switch (type)
        {
            case "object":
                ValidateObjectSchema(schema, path, depth);
                break;
            case "array":
                ValidateArraySchema(schema, path, depth);
                break;
            case "string":
                ValidateStringSchema(schema, path);
                break;
            case "number":
            case "integer":
                ValidateNumberSchema(schema, path);
                break;
        }
    }

    private static string RequiredType(JsonElement schema, string path)
    {
        if (!schema.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String
            || !SupportedTypes.Contains(typeElement.GetString() ?? string.Empty))
            throw Failure("ai.result_schema.type_invalid", ChildPath(path, "type"),
                "The AI result schema requires one supported scalar type.");
        return typeElement.GetString()!;
    }

    private static void ValidateMetadata(JsonElement schema, string path)
    {
        foreach (var keyword in new[] { "title", "description" })
        {
            if (!schema.TryGetProperty(keyword, out var value)) continue;
            if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 1_000)
                throw Failure("ai.result_schema.keyword_invalid", ChildPath(path, keyword),
                    "The AI result schema metadata is invalid.");
        }
    }

    private static void ValidateObjectSchema(JsonElement schema, string path, int depth)
    {
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("properties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object)
                throw InvalidKeyword(path, "properties");
            var count = 0;
            foreach (var property in properties.EnumerateObject())
            {
                count++;
                if (count > MaximumProperties || property.Name.Length is 0 or > 200)
                    throw Failure("ai.result_schema.too_complex", ChildPath(path, "properties"),
                        "The AI result schema exceeds the supported complexity.");
                if (!propertyNames.Add(property.Name))
                    throw Failure("ai.result_schema.duplicate_property", ChildPath(path, property.Name),
                        "The AI result schema contains a duplicate property.");
                ValidateSchemaNode(
                    property.Value,
                    ChildPath(ChildPath(path, "properties"), property.Name),
                    depth + 1);
            }
        }

        if (schema.TryGetProperty("required", out var required))
        {
            if (required.ValueKind != JsonValueKind.Array) throw InvalidKeyword(path, "required");
            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || !requiredNames.Add(item.GetString() ?? string.Empty)
                    || !propertyNames.Contains(item.GetString() ?? string.Empty))
                    throw InvalidKeyword(path, "required");
            }
        }

        if (schema.TryGetProperty("additionalProperties", out var additionalProperties)
            && additionalProperties.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw InvalidKeyword(path, "additionalProperties");
    }

    private static void ValidateArraySchema(JsonElement schema, string path, int depth)
    {
        if (!schema.TryGetProperty("items", out var items))
            throw Failure("ai.result_schema.items_required", ChildPath(path, "items"),
                "Array schemas require an items schema.");
        ValidateSchemaNode(items, $"{path}[]", depth + 1);

        var minimum = OptionalNonNegativeInteger(schema, "minItems", path, MaximumCollectionItems);
        var maximum = OptionalNonNegativeInteger(schema, "maxItems", path, MaximumCollectionItems);
        EnsureOrdered(minimum, maximum, path, "maxItems");
    }

    private static void ValidateStringSchema(JsonElement schema, string path)
    {
        var minimum = OptionalNonNegativeInteger(schema, "minLength", path, MaximumResultLength);
        var maximum = OptionalNonNegativeInteger(schema, "maxLength", path, MaximumResultLength);
        EnsureOrdered(minimum, maximum, path, "maxLength");
    }

    private static void ValidateNumberSchema(JsonElement schema, string path)
    {
        var minimum = OptionalNumber(schema, "minimum", path);
        var maximum = OptionalNumber(schema, "maximum", path);
        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw InvalidKeyword(path, "maximum");
    }

    private static void ValidateEnum(JsonElement schema, string path, string type)
    {
        if (!schema.TryGetProperty("enum", out var values)) return;
        if (values.ValueKind != JsonValueKind.Array) throw InvalidKeyword(path, "enum");
        var serialized = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var value in values.EnumerateArray())
        {
            count++;
            if (count > MaximumEnumValues || !MatchesType(type, value)
                || !serialized.Add(value.GetRawText()))
                throw InvalidKeyword(path, "enum");
        }
        if (count == 0) throw InvalidKeyword(path, "enum");
    }

    private static void ValidateKeywordApplicability(JsonElement schema, string path, string type)
    {
        var allowedForType = type switch
        {
            "object" => new[] { "properties", "required", "additionalProperties" },
            "array" => new[] { "items", "minItems", "maxItems" },
            "string" => new[] { "minLength", "maxLength" },
            "number" or "integer" => new[] { "minimum", "maximum" },
            _ => []
        };
        foreach (var property in schema.EnumerateObject())
        {
            if (property.Name is "type" or "title" or "description" or "enum"
                || allowedForType.Contains(property.Name, StringComparer.Ordinal))
                continue;
            throw InvalidKeyword(path, property.Name);
        }
    }

    private static void ValidateResultNode(JsonElement schema, JsonElement result, string path, int depth)
    {
        EnsureDepth(depth, schema: false, path);
        var type = schema.GetProperty("type").GetString()!;
        if (!MatchesType(type, result))
            throw Failure("ai.result.type", path, "The AI result has an unexpected type.");

        if (schema.TryGetProperty("enum", out var values)
            && !values.EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, result)))
            throw Failure("ai.result.enum", path, "The AI result is outside the allowed values.");

        switch (type)
        {
            case "object":
                ValidateResultObject(schema, result, path, depth);
                break;
            case "array":
                ValidateResultArray(schema, result, path, depth);
                break;
            case "string":
                ValidateResultString(schema, result, path);
                break;
            case "number":
            case "integer":
                ValidateResultNumber(schema, result, path);
                break;
        }
    }

    private static void ValidateResultObject(JsonElement schema, JsonElement result, string path, int depth)
    {
        var properties = schema.TryGetProperty("properties", out var configuredProperties)
            ? configuredProperties
            : default;
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var item in required.EnumerateArray())
            {
                var name = item.GetString()!;
                if (!result.TryGetProperty(name, out _))
                    throw Failure("ai.result.required", ChildPath(path, name),
                        "The AI result is missing a required property.");
            }
        }

        var allowAdditional = !schema.TryGetProperty("additionalProperties", out var additional)
                              || additional.GetBoolean();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in result.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw Failure("ai.result.duplicate_property", ChildPath(path, property.Name),
                    "The AI result contains a duplicate property.");
            if (properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty(property.Name, out var childSchema))
            {
                ValidateResultNode(childSchema, property.Value, ChildPath(path, property.Name), depth + 1);
            }
            else if (!allowAdditional)
            {
                throw Failure("ai.result.additional_property", ChildPath(path, property.Name),
                    "The AI result contains an additional property.");
            }
        }
    }

    private static void ValidateResultArray(JsonElement schema, JsonElement result, string path, int depth)
    {
        var length = result.GetArrayLength();
        EnsureMinimum(schema, "minItems", length, path, "ai.result.min_items");
        EnsureMaximum(schema, "maxItems", length, path, "ai.result.max_items");
        var index = 0;
        foreach (var item in result.EnumerateArray())
        {
            ValidateResultNode(schema.GetProperty("items"), item, $"{path}[{index}]", depth + 1);
            index++;
        }
    }

    private static void ValidateResultString(JsonElement schema, JsonElement result, string path)
    {
        var length = result.GetString()!.EnumerateRunes().Count();
        EnsureMinimum(schema, "minLength", length, path, "ai.result.min_length");
        EnsureMaximum(schema, "maxLength", length, path, "ai.result.max_length");
    }

    private static void ValidateResultNumber(JsonElement schema, JsonElement result, string path)
    {
        var value = result.GetDouble();
        if (schema.TryGetProperty("minimum", out var minimum) && value < minimum.GetDouble())
            throw Failure("ai.result.minimum", path, "The AI result is below the configured minimum.");
        if (schema.TryGetProperty("maximum", out var maximum) && value > maximum.GetDouble())
            throw Failure("ai.result.maximum", path, "The AI result exceeds the configured maximum.");
    }

    private static bool MatchesType(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number
                    && value.TryGetDouble(out var number)
                    && double.IsFinite(number),
        "integer" => value.ValueKind == JsonValueKind.Number && IsInteger(value),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };

    private static bool IsInteger(JsonElement value)
    {
        if (value.TryGetInt64(out _)) return true;
        return value.TryGetDouble(out var number)
               && double.IsFinite(number)
               && Math.Truncate(number) == number;
    }

    private static int? OptionalNonNegativeInteger(JsonElement schema, string keyword, string path, int maximum)
    {
        if (!schema.TryGetProperty(keyword, out var value)) return null;
        if (!value.TryGetInt32(out var parsed) || parsed < 0 || parsed > maximum)
            throw InvalidKeyword(path, keyword);
        return parsed;
    }

    private static double? OptionalNumber(JsonElement schema, string keyword, string path)
    {
        if (!schema.TryGetProperty(keyword, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Number) throw InvalidKeyword(path, keyword);
        var parsed = value.GetDouble();
        if (!double.IsFinite(parsed)) throw InvalidKeyword(path, keyword);
        return parsed;
    }

    private static void EnsureOrdered(int? minimum, int? maximum, string path, string maximumName)
    {
        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw InvalidKeyword(path, maximumName);
    }

    private static void EnsureMinimum(JsonElement schema, string keyword, int value, string path, string code)
    {
        if (schema.TryGetProperty(keyword, out var minimum) && value < minimum.GetInt32())
            throw Failure(code, path, "The AI result is shorter than the configured minimum.");
    }

    private static void EnsureMaximum(JsonElement schema, string keyword, int value, string path, string code)
    {
        if (schema.TryGetProperty(keyword, out var maximum) && value > maximum.GetInt32())
            throw Failure(code, path, "The AI result exceeds the configured maximum.");
    }

    private static void EnsureDepth(int depth, bool schema, string path)
    {
        if (depth <= MaximumDepth) return;
        throw Failure(schema ? "ai.result_schema.too_complex" : "ai.result.too_complex", path,
            schema
                ? "The AI result schema exceeds the supported complexity."
                : "The AI result exceeds the supported complexity.");
    }

    private static bool HasType(JsonElement schema, string expected) =>
        schema.TryGetProperty("type", out var type)
        && string.Equals(type.GetString(), expected, StringComparison.Ordinal);

    private static AiResultSchemaException InvalidKeyword(string path, string keyword) =>
        Failure("ai.result_schema.keyword_invalid", ChildPath(path, keyword),
            "The AI result schema contains an invalid keyword value.");

    private static string ChildPath(string path, string property) =>
        property.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '$')
            ? $"{path}.{property}"
            : $"{path}['{property.Replace("'", "\\'", StringComparison.Ordinal)}']";

    private static AiResultSchemaException Failure(string code, string path, string message) =>
        new(code, path, message);
}

/// <summary>Stabil klassifizierter Schema- oder Ergebnisfehler ohne fremde Rohdaten.</summary>
public sealed class AiResultSchemaException(string code, string path, string message) : Exception(message)
{
    public string Code { get; } = code;
    public string Path { get; } = path;
}
