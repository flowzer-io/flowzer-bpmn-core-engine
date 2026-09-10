namespace WebApiEngine.Shared;

public enum AiToolSideEffectDto
{
    ReadOnly,
    Write,
    Send
}

/// <summary>
/// Nicht geheimer Katalogvertrag einer installierten Werkzeugversion. Interne Handler,
/// Zielsystemkonfigurationen und Zugangsdaten werden nicht projiziert.
/// </summary>
public sealed record AiToolDto
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string InputSchema { get; init; }
    public required string OutputSchema { get; init; }
    public required AiToolSideEffectDto SideEffect { get; init; }
    public required bool AllowsPreApproval { get; init; }
    public required string ContractHash { get; init; }
}
