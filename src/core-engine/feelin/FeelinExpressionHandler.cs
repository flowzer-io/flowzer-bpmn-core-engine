using BPMN.Common;
using Microsoft.ClearScript.JavaScript;
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

    public object? GetValue(object? obj, string expression)
    {
        if (!expression.StartsWith("="))
            return expression;

        obj ??= new Variables();
        
        _jsEngine.AddHostObject("vars", (dynamic)obj);
     
        var expressionWithoutAssignmentSign = JsonQuote(expression.Substring(1));
        var script = $$$"""
                              libfeelin.evaluate("{{{expressionWithoutAssignmentSign}}}", vars)
                              """;
        
        var ret = _jsEngine.Evaluate(script);
        if (ExpandoHelper.IsComlexValue(ret))
            return ret.ToDynamic();
        else
        {
            return ret;
        }
    }

    private string JsonQuote(string str)
    {
        return str.Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    public bool MatchExpression(ProcessInstance processInstance, Expression expression)
    {
       
        _jsEngine.AddHostObject("vars", (dynamic)processInstance.ProcessVariables);
        var resultValue = _jsEngine.Evaluate($$$"""
                                                libfeelin.unaryTest({{{expression.Body}}}, obj)
                                                """).ToString();
        return resultValue == "true";
    }
}