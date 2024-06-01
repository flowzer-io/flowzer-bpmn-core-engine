using System.Text.RegularExpressions;

namespace core_engine;

public abstract partial class ExpressionHandler
{
    /// <summary>
    /// Get the value by expression from the process variables 
    /// </summary>
    public abstract object? GetValue(object obj, string expression);

    /// <summary>
    /// Checks if the expression matches the conditions of the sequence flow
    /// </summary>
    public abstract bool MatchExpression(object obj, string expression);

    /// <summary>
    /// Checks if the expression has values to be resolved and replace them
    /// This is, if the string has {{Expression}} Values or starts with =
    /// </summary>
    /// <param name="obj">Global Variables to use for resolving</param>
    /// <param name="expression">Expression String for resolving</param>
    /// <returns>Expression String with resolved Values</returns>
    public string ResolveString(object obj, string expression)
    {
        if (expression.StartsWith('='))
            return GetValue(obj, expression[1..])?.ToString() ?? expression;

        // Regex to match {{Expression}} in the string
        var regex = DoubleCurlyBraceRegex();
        var matches = regex.Matches(expression);
        foreach (Match match in matches)
        {
            var value = GetValue(obj, match.Groups[1].Value);
            expression = expression.Replace(match.Value, value?.ToString() ?? "");
        }

        return expression;
    }

    [GeneratedRegex("{{(.*?)}}")]
    private static partial Regex DoubleCurlyBraceRegex();
}