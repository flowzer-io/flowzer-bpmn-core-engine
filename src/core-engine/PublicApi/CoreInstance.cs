namespace core_engine;

/// <summary>
/// Öffentliche Sicht auf eine laufende oder abgeschlossene Instanz.
/// </summary>
public class CoreInstance
{
    public required Guid Id { get; init; }

    public required ProcessInstanceState State { get; init; }

    public required IReadOnlyList<CoreInteraction> Interactions { get; init; }
}
