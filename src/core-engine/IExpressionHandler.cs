using System.Security.Cryptography;
using BPMN.Common;
using BPMN.Process;
using Model;
using Newtonsoft.Json.Linq;

namespace core_engine;

public interface IExpressionHandler
{
    /// <summary>
    /// Get the value by expression from the process variables 
    /// </summary>
    JToken? GetValue(JToken @object, string expression);
    
    /// <summary>
    /// Checks if the expression matches the conditions of the sequence flow
    /// </summary>
    bool MatchExpression(ProcessInstance processInstance, Expression? expression);
}