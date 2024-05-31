using BPMN.Common;
using Microsoft.ClearScript.V8;
using Model;
using Newtonsoft.Json.Linq;

namespace core_engine;


public class JavaScriptV8ExpressionHandler : IExpressionHandler
{
   
    public object? GetValue(object? obj, string expression)
    {
        if (obj == null)
            return null;
        using var engine = new V8ScriptEngine();
        engine.AddHostObject("globals", obj);
        var fullExpression = "globals." + expression;
        return JToken.FromObject(engine.Evaluate(fullExpression));   
    }

 

    public bool MatchExpression(object obj, string expression)
    {
        return string.Compare(GetValue(obj, expression) as string, "true",
            StringComparison.InvariantCultureIgnoreCase) == 0;
    }
}