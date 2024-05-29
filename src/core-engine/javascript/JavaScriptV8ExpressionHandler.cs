using BPMN.Common;
using Microsoft.ClearScript.V8;
using Model;
using Newtonsoft.Json.Linq;

namespace core_engine;


public class JavaScriptV8ExpressionHandler : IExpressionHandler
{
   
    public JToken? GetValue(JToken? @object, string expression)
    {
        if (@object == null)
            return null;
        using var engine = new V8ScriptEngine();
        engine.AddHostObject("globals", @object);
        var fullExpression = "globals." + expression;
        return JToken.FromObject(engine.Evaluate(fullExpression));   
    }

 

    public bool MatchExpression(ProcessInstance processInstance, Expression? expression)
    {
        return expression switch
        {
            FormalExpression => throw new Exception("FormalExpressions are not supported in JavaScriptV8ExpressionHandler"),
            null => true,
            _ => GetValue(processInstance.ProcessVariables, expression.Body).Value<bool>()
        };
    }
}