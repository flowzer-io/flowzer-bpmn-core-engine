using System.Dynamic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flowzer.Shared;

namespace core_engine.Expression;

/// <summary>
/// Einfacher Expression Handler für Tests - benötigt keine V8 Engine
/// </summary>
public partial class SimpleExpressionHandler : DefaultExpressionHandler
{
    public override object? GetValue(object? obj, string expression)
    {
        // Entferne führendes '=' falls vorhanden
        if (expression.StartsWith('='))
        {
            expression = expression.Substring(1);
            
            // Einfache numerische Werte
            if (int.TryParse(expression, out var intValue))
                return intValue;
            if (double.TryParse(expression, out var doubleValue))
                return doubleValue;
            if (bool.TryParse(expression, out var boolValue))
                return boolValue;
            
            // Versuche JSON zu parsen falls es mit { oder [ beginnt
            if (expression.Trim().StartsWith('{') || expression.Trim().StartsWith('['))
            {
                try
                {
                    var jsonElement = JsonSerializer.Deserialize<JsonElement>(expression);
                    return ConvertJsonElementToExpandoObject(jsonElement);
                }
                catch
                {
                    // Falls JSON Parsing fehlschlägt, fahre mit normalem Variablenzugriff fort
                }
            }
            
            // Falls es sich um einen Variablenzugriff handelt und obj ist nicht null
            if (obj != null)
            {
                // Einfache Eigenschaftszugriffe wie "variableName" oder "object.property"
                if (obj is ExpandoObject variables)
                {
                    return variables.GetValue(expression);
                }
                
                // Fallback: Versuche Eigenschaft via Reflection zu finden
                try
                {
                    var type = obj.GetType();
                    var property = type.GetProperty(expression);
                    return property?.GetValue(obj);
                }
                catch
                {
                    // Ignore
                }
            }
            
            // Falls keine Variable gefunden, return as literal string
            return expression;
        }
        
        // Ohne '=' - direkte Eigenschaftszugriffe
        if (obj == null)
            return null;
            
        // Einfache Eigenschaftszugriffe wie "variableName" oder "object.property"
        if (obj is ExpandoObject variables2)
        {
            return variables2.GetValue(expression);
        }
        
        // Fallback: Versuche Eigenschaft via Reflection zu finden
        try
        {
            var type = obj.GetType();
            var property = type.GetProperty(expression);
            return property?.GetValue(obj);
        }
        catch
        {
            return expression; // Fallback: Gib den Expression String zurück
        }
    }

    public override bool MatchExpression(object obj, string expression)
    {
        if (expression.StartsWith('='))
        {
            // Handle expressions like "=Order.Address.Firstname = 'Lukas'"
            var cleanExpression = expression.Substring(1).Trim();
            
            // Check if it's a comparison expression (contains = but not ==)
            if (cleanExpression.Contains("=") && !cleanExpression.Contains("=="))
            {
                // Split on = but handle both "var=value" and "var = value"
                var splitIndex = cleanExpression.IndexOf('=');
                if (splitIndex > 0 && splitIndex < cleanExpression.Length - 1)
                {
                    var leftPart = cleanExpression.Substring(0, splitIndex).Trim();
                    var rightPart = cleanExpression.Substring(splitIndex + 1).Trim();
                    
                    var leftValue = GetValue(obj, "=" + leftPart);
                    var rightValue = rightPart.Trim('"', '\''); // Remove quotes if present
                    
                    return string.Equals(leftValue?.ToString(), rightValue, StringComparison.InvariantCultureIgnoreCase);
                }
            }
            
            // For other expressions, try to evaluate and check if result is "true"
            var value = GetValue(obj, expression);
            
            // Handle boolean values
            if (value is bool boolValue)
                return boolValue;
                
            // Handle string values that represent true/false
            return string.Compare(value?.ToString(), "true", StringComparison.InvariantCultureIgnoreCase) == 0;
        }
        
        // Direkte Vergleiche ohne '='
        return string.Compare(expression, "true", StringComparison.InvariantCultureIgnoreCase) == 0;
    }

    private static object? ConvertJsonElementToExpandoObject(JsonElement jsonElement)
    {
        return jsonElement.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObjectToExpandoObject(jsonElement),
            JsonValueKind.Array => ConvertJsonArrayToList(jsonElement),
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement.TryGetInt64(out var longValue) ? longValue : jsonElement.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => jsonElement.ToString()
        };
    }

    private static ExpandoObject ConvertJsonObjectToExpandoObject(JsonElement jsonObject)
    {
        var expandoObject = new ExpandoObject();
        var dict = (IDictionary<string, object?>)expandoObject;

        foreach (var property in jsonObject.EnumerateObject())
        {
            dict[property.Name] = ConvertJsonElementToExpandoObject(property.Value);
        }

        return expandoObject;
    }

    private static List<object?> ConvertJsonArrayToList(JsonElement jsonArray)
    {
        var list = new List<object?>();
        foreach (var element in jsonArray.EnumerateArray())
        {
            list.Add(ConvertJsonElementToExpandoObject(element));
        }
        return list;
    }
}