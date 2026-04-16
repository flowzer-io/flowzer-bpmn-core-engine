using System.Dynamic;
using System.Text.Json;
using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Übersetzt einen ausgewählten Token in gut lesbare Textdarstellungen für die Instanzansicht.
/// </summary>
public static class TokenSelectionViewHelper
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true
    };

    public static TokenSelectionDetails Create(TokenDto? token)
    {
        if (token == null)
        {
            return new TokenSelectionDetails(
                "Select a token",
                string.Empty,
                string.Empty,
                string.Empty,
                HasVariables: false,
                HasOutputData: false);
        }

        return new TokenSelectionDetails(
            TokenDisplayHelper.GetDisplayName(token, "(unnamed token)"),
            TokenDisplayHelper.GetFlowElementId(token) ?? string.Empty,
            Serialize(token.Variables, out var hasVariables),
            Serialize(token.OutputData, out var hasOutputData),
            hasVariables,
            hasOutputData);
    }

    private static string Serialize(ExpandoObject? data, out bool hasData)
    {
        if (data is not IDictionary<string, object?> values || values.Count == 0)
        {
            hasData = false;
            return string.Empty;
        }

        hasData = true;
        return JsonSerializer.Serialize(data, JsonSerializerOptions);
    }
}

public sealed record TokenSelectionDetails(
    string Title,
    string FlowNodeId,
    string VariablesJson,
    string OutputJson,
    bool HasVariables,
    bool HasOutputData);
