using BPMN.Common;
using Microsoft.ClearScript.V8;
using Model;
using Newtonsoft.Json.Linq;

namespace core_engine;

public class FeelinExpressionHandler : IExpressionHandler
{
    private V8ScriptEngine _jsEngine;

    public FeelinExpressionHandler()
    {
        _jsEngine = new V8ScriptEngine();
        _jsEngine.Execute(File.ReadAllText("feelin/bundle.js"));
    }

    public JToken? GetValue(JObject obj, string expression)
    {
        if (!expression.StartsWith("="))
            return JToken.FromObject(expression);
        
        
        _jsEngine.AddHostObject("vars", (dynamic)obj);
        
     
        var expressionWithoutAssignmentSign = expression.Substring(1);
        var svript = $$$"""
                              libfeelin.evaluate("{{{expressionWithoutAssignmentSign}}}", vars)
                              """;
        var resultValue = _jsEngine.Evaluate(svript);

        return JToken.FromObject(resultValue);
        
    }

    public bool MatchExpression(ProcessInstance processInstance, Expression? expression)
    {
        if (expression == null)
            return true;
        
        _jsEngine.AddHostObject("vars", (dynamic)processInstance.ProcessVariables);
        var resultValue = _jsEngine.Evaluate($$$"""
                                                libfeelin.unaryTest({{{expression.Body}}}, obj)
                                                """).ToString();
        return resultValue == "true";
    }
}