using BPMN.Common;
using Microsoft.ClearScript.V8;
using Newtonsoft.Json.Linq;

namespace core_engine;



public class JavaScriptV8ExpressionHandler : IExpresssionHandler
{
   
    public JToken GetValue(ProcessInstance processInstance, string expression)
    {
        using var engine = new V8ScriptEngine();
        engine.AddHostObject("globals", processInstance.ProcessVariables );
        var fullExpression = "globals." + expression;
        return JToken.FromObject(engine.Evaluate(fullExpression));   
    }

    public bool MatchExpression(ProcessInstance processInstance, Expression? expression)
    {
        return expression switch
        {
            FormalExpression => throw new Exception("FormalExpressions are not supported in JavaScriptV8ExpressionHandler"),
            null => true,
            _ => GetValue(processInstance, expression.Body).Value<bool>()
        };
    }
}