using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using core_engine;
using Model;

namespace WebApiEngine.Ai;

internal sealed record RegisteredAiTool(
    AiToolDefinition Definition,
    string ContractHash,
    IAiTool Implementation);

/// <summary>
/// Geschlossene Registry aller in dieser Installation verfuegbaren Werkzeuge. IDs und
/// Versionen sind eindeutig; der Vertragshash bindet spaeter exakt das gepruefte Schema.
/// </summary>
public sealed class AiToolRegistry
{
    private static readonly Regex IdPattern = new(
        "^[a-z0-9][a-z0-9._-]{0,99}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly IReadOnlyDictionary<(string Id, int Version), RegisteredAiTool> _tools;
    private readonly IReadOnlyList<RegisteredAiTool> _ordered;

    public AiToolRegistry(IEnumerable<IAiTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var registered = tools.Select(Register).ToArray();
        var duplicate = registered
            .GroupBy(item => (item.Definition.Id, item.Definition.Version))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"The AI tool registry contains duplicate identity '{duplicate.Key.Id}' v{duplicate.Key.Version}.");

        _ordered = registered
            .OrderBy(item => item.Definition.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Definition.Version)
            .ToArray();
        _tools = _ordered.ToDictionary(item => (item.Definition.Id, item.Definition.Version));
    }

    internal IReadOnlyList<RegisteredAiTool> List() => _ordered;

    internal RegisteredAiTool? Find(string id, int version) =>
        _tools.GetValueOrDefault((id, version));

    internal RegisteredAiTool Require(string id, int version) =>
        Find(id, version)
        ?? throw new KeyNotFoundException($"AI tool '{id}' v{version} is not registered.");

    private static RegisteredAiTool Register(IAiTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var definition = tool.Definition ?? throw new ArgumentException("AI tool definition is required.", nameof(tool));
        if (!IdPattern.IsMatch(definition.Id))
            throw new ArgumentException("AI tool id is invalid.", nameof(tool));
        if (definition.Version < 1)
            throw new ArgumentException("AI tool version must be positive.", nameof(tool));
        if (!Enum.IsDefined(definition.SideEffect))
            throw new ArgumentException("AI tool side effect is invalid.", nameof(tool));
        ValidateText(definition.Name, 200, "name");
        ValidateText(definition.Description, 2_000, "description");
        try
        {
            AiResultSchemaProfile.ValidateSchema(definition.InputSchema);
            AiResultSchemaProfile.ValidateSchema(definition.OutputSchema);
        }
        catch (Exception exception) when (exception is AiResultSchemaException or ArgumentException)
        {
            throw new ArgumentException("AI tool schema is outside the supported Flowzer profile.", nameof(tool), exception);
        }

        return new RegisteredAiTool(definition, Hash(definition), tool);
    }

    private static void ValidateText(string value, int maximumLength, string property)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(char.IsControl))
            throw new ArgumentException($"AI tool {property} is invalid.");
    }

    private static string Hash(AiToolDefinition definition)
    {
        var material = string.Join('\n',
            definition.Id,
            definition.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            definition.Name,
            definition.Description,
            definition.InputSchema,
            definition.OutputSchema,
            definition.SideEffect.ToString(),
            definition.AllowsPreApproval ? "1" : "0");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
