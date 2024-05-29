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

    public JToken? GetValue(JToken @object, string expression)
    {
        _jsEngine.Script.
        _jsEngine.Evaluate(@"
                           return libfeelin.evaluate(""Mike's dauther.name"", {
                             'Mike\'s dauther.name': 'Lisa'}));
                           ");
        return null;
    }

    public bool MatchExpression(ProcessInstance processInstance, Expression? expression)
    {
        return false;
    }
}