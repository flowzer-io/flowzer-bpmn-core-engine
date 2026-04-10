using System.Globalization;
using System.Dynamic;
using System.Text.Json;
using Flowzer.Shared;

namespace core_engine.Expression;

/// <summary>
/// Vereinfachter Expression-Handler für Tests und Fallback-Szenarien ohne V8.
/// Er deckt bewusst nur die aktuell im Testbestand verwendeten Fälle ab.
/// </summary>
public sealed class SimpleExpressionHandler : DefaultExpressionHandler
{
    public override object? GetValue(object obj, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return expression;

        var normalizedExpression = NormalizeExpression(expression);
        if (TryParseLiteral(normalizedExpression, out var literalValue))
            return literalValue;

        var resolvedValue = ResolveMemberAccess(obj, normalizedExpression);
        return resolvedValue ?? normalizedExpression;
    }

    public override bool MatchExpression(object obj, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var normalizedExpression = NormalizeExpression(expression);
        if (TrySplitComparison(normalizedExpression, out var leftPart, out var rightPart))
        {
            var leftValue = ResolveValue(obj, leftPart);
            var rightValue = ResolveValue(obj, rightPart);
            return string.Equals(leftValue?.ToString(), rightValue?.ToString(), StringComparison.InvariantCultureIgnoreCase);
        }

        var expressionValue = ResolveValue(obj, normalizedExpression);
        return expressionValue switch
        {
            bool booleanValue => booleanValue,
            string stringValue when bool.TryParse(stringValue, out var parsedBoolean) => parsedBoolean,
            _ => string.Equals(expressionValue?.ToString(), "true", StringComparison.InvariantCultureIgnoreCase)
        };
    }

    private static object? ResolveValue(object obj, string expressionPart)
    {
        if (TryParseLiteral(expressionPart, out var literalValue))
            return literalValue;

        return ResolveMemberAccess(obj, expressionPart);
    }

    private static string NormalizeExpression(string expression)
    {
        return expression.StartsWith('=') ? expression[1..].Trim() : expression.Trim();
    }

    private static object? ResolveMemberAccess(object obj, string expression)
    {
        if (obj is null)
            return null;

        if (obj is ExpandoObject expandoObject)
            return expandoObject.GetValue(expression);

        if (ExpandoHelper.IsComlexValue(obj))
        {
            var dynamicObject = obj.ToDynamic(true) as ExpandoObject;
            return dynamicObject?.GetValue(expression);
        }

        var property = obj.GetType().GetProperty(expression);
        return property?.GetValue(obj);
    }

    private static bool TryParseLiteral(string expression, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        if ((expression.StartsWith('{') && expression.EndsWith('}')) ||
            (expression.StartsWith('[') && expression.EndsWith(']')))
        {
            try
            {
                using var document = JsonDocument.Parse(expression);
                value = ConvertJsonElement(document.RootElement);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if ((expression.StartsWith('"') && expression.EndsWith('"')) ||
            (expression.StartsWith('\'') && expression.EndsWith('\'')))
        {
            value = expression[1..^1];
            return true;
        }

        if (bool.TryParse(expression, out var booleanValue))
        {
            value = booleanValue;
            return true;
        }

        if (int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            value = intValue;
            return true;
        }

        if (double.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            value = doubleValue;
            return true;
        }

        return false;
    }

    private static bool TrySplitComparison(string expression, out string leftPart, out string rightPart)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index < expression.Length; index++)
        {
            var currentCharacter = expression[index];

            if (currentCharacter == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (currentCharacter == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (currentCharacter != '=' || inSingleQuote || inDoubleQuote)
                continue;

            leftPart = expression[..index].Trim();
            rightPart = expression[(index + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(leftPart) && !string.IsNullOrWhiteSpace(rightPart);
        }

        leftPart = string.Empty;
        rightPart = string.Empty;
        return false;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(element),
            JsonValueKind.Array => ConvertJsonArray(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static ExpandoObject ConvertJsonObject(JsonElement jsonObject)
    {
        var dynamicObject = new ExpandoObject();
        var dictionary = (IDictionary<string, object?>)dynamicObject;

        foreach (var property in jsonObject.EnumerateObject())
        {
            dictionary[property.Name] = ConvertJsonElement(property.Value);
        }

        return dynamicObject;
    }

    private static List<object?> ConvertJsonArray(JsonElement jsonArray)
    {
        return jsonArray.EnumerateArray().Select(ConvertJsonElement).ToList();
    }
}
