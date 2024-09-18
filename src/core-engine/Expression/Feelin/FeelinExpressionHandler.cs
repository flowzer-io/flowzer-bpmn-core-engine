
namespace core_engine.Expression.Feelin;

using Microsoft.ClearScript.V8;

public class FeelinExpressionHandler : DefaultExpressionHandler
{
    private readonly V8ScriptEngine _jsEngine;

    public FeelinExpressionHandler()
    {
        _jsEngine = new V8ScriptEngine();
        var directory = Path.GetDirectoryName(GetType().Assembly.Location);
        if (directory == null)
            throw new IOException("Could not find the directory of the assembly");
        var fullPath = Path.Combine(directory, "Expression/Feelin/bundle.js");
        _jsEngine.Execute(File.ReadAllText(fullPath));
    }

    public override object? GetValue(object? obj, string expression)
    {
        if (!expression.StartsWith('='))
            return expression;

        obj ??= new Variables();
        
        _jsEngine.AddHostObject("vars", (dynamic)obj);
     
        var expressionWithoutAssignmentSign = JsonQuote(expression.Substring(1));
        var script = $"""libfeelin.evaluate("{expressionWithoutAssignmentSign}", vars)""";
        
        var ret = _jsEngine.Evaluate(script);
        return ExpandoHelper.IsComlexValue(ret) ? ret.ToDynamic() : ret;
    }

    private static string JsonQuote(string str)
    {
        return str.Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    public override bool MatchExpression(object obj, string expression)
    {
       
        if (!expression.StartsWith('='))
            return string.Compare(expression, "true", StringComparison.InvariantCultureIgnoreCase) == 0;
        
        expression = expression.Substring(1);
        expression = JsonQuote(expression);
        _jsEngine.AddHostObject("vars", obj);
        var fullScript = $"""libfeelin.unaryTest("{expression}", vars)""";
        var resultValue = _jsEngine.Evaluate(fullScript).ToString();
        return string.Compare( resultValue, "true", StringComparison.InvariantCultureIgnoreCase) == 0;
    }
}