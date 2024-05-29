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
        var directory = System.IO.Path.GetDirectoryName(GetType().Assembly.Location);
        if (directory == null)
            throw new IOException("Could not find the directory of the assembly");
        var fullPath = System.IO.Path.Combine(directory, "feelin/bundle.js");
        _jsEngine.Execute(File.ReadAllText(fullPath));
    }

    public object? GetValue(object obj, string expression)
    {
        if (!expression.StartsWith("="))
            return expression;
        
        
        _jsEngine.AddHostObject("vars", (dynamic)obj);
        
     
        var expressionWithoutAssignmentSign = expression.Substring(1);
        var svript = $$$"""
                              libfeelin.evaluate("{{{expressionWithoutAssignmentSign}}}", vars)
                              """;
        return _jsEngine.Evaluate(svript);


        
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