using System.Text.RegularExpressions;

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
            
            // Falls es sich um einen Variablenzugriff handelt und obj ist nicht null
            if (obj != null)
            {
                // Einfache Eigenschaftszugriffe wie "variableName" oder "object.property"
                if (obj is Variables variables)
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
        if (obj is Variables variables2)
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
            var value = GetValue(obj, expression);
            return string.Compare(value?.ToString(), "true", StringComparison.InvariantCultureIgnoreCase) == 0;
        }
        
        // Direkte Vergleiche ohne '='
        return string.Compare(expression, "true", StringComparison.InvariantCultureIgnoreCase) == 0;
    }
}