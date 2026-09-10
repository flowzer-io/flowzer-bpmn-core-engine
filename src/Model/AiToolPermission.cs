namespace Model;

/// <summary>Auswirkung eines Werkzeugs ausserhalb des reinen Modellkontexts.</summary>
public enum AiToolSideEffect
{
    ReadOnly,
    Write,
    Send
}

/// <summary>
/// Administrativ erlaubte Werkzeugversion einer KI-Verbindung. Die Vorabfreigabe ist eine
/// zusätzliche Grenze und wird nie allein aus einem Workflow oder Prompt abgeleitet.
/// </summary>
public sealed record AiToolPermission(
    string ToolId,
    int ToolVersion,
    bool AllowPreApproval);

/// <summary>Beim Deployment unveraenderlich gebundener, nicht geheimer Werkzeugvertrag.</summary>
public sealed record BoundAiTool(
    string ToolId,
    int ToolVersion,
    string ContractHash,
    AiToolSideEffect SideEffect,
    BPMN.Flowzer.AiToolApprovalMode ApprovalMode);

public static class AiToolPermissionRules
{
    public static IReadOnlyList<AiToolPermission> Effective(AiConnection connection) =>
        connection.AllowedTools ?? [];

    public static bool Same(
        IReadOnlyList<AiToolPermission>? left,
        IReadOnlyList<AiToolPermission>? right) =>
        (left ?? []).SequenceEqual(right ?? []);

    public static bool Same(AiConnection left, AiConnection right) =>
        left.Id == right.Id
        && left.Name == right.Name
        && left.Provider == right.Provider
        && left.Location == right.Location
        && left.BaseAddress == right.BaseAddress
        && left.DefaultModel == right.DefaultModel
        && left.SecretReference == right.SecretReference
        && left.Enabled == right.Enabled
        && left.Revision == right.Revision
        && left.UpdatedAtUtc == right.UpdatedAtUtc
        && left.UpdatedByUserId == right.UpdatedByUserId
        && Same(left.AllowedTools, right.AllowedTools);

    public static void Validate(IReadOnlyList<AiToolPermission>? permissions)
    {
        var values = permissions ?? [];
        if (values.Count > 50)
            throw new ArgumentException("An AI connection supports at most 50 tool permissions.", nameof(permissions));
        if (values.Any(permission => permission is null
                                     || !ValidId(permission.ToolId)
                                     || permission.ToolVersion < 1))
            throw new ArgumentException("AI tool permission is invalid.", nameof(permissions));
        if (values.Select(permission => (permission.ToolId, permission.ToolVersion)).Distinct().Count() != values.Count)
            throw new ArgumentException("AI tool permissions must be unique.", nameof(permissions));
        var ordered = values
            .OrderBy(permission => permission.ToolId, StringComparer.Ordinal)
            .ThenBy(permission => permission.ToolVersion);
        if (!values.SequenceEqual(ordered))
            throw new ArgumentException("AI tool permissions must use canonical ordering.", nameof(permissions));
    }

    private static bool ValidId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 100
        && id[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
        && id.All(character => character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.' or '_' or '-');
}
