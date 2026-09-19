using Model;

namespace StorageSystem;

/// <summary>
/// Ablage der Entscheidungsdateien: je Datei ein Katalogkopf und daran haengend die
/// unveraenderlichen XML-Staende.
/// </summary>
/// <remarks>
/// <para>
/// Die Kennung einer Datei (<see cref="DecisionDefinition.DecisionDefinitionId"/>) muss
/// <see cref="DefinitionIdRules"/> genuegen — genau wie bei BPMN. In der Dateiablage wird sie
/// zum Dateinamen; eine Kennung wie <c>../../x</c> zeigte aus dem Ablageordner heraus, und das
/// Speichern oder Loeschen traefe eine fremde Datei (CodeQL <c>cs/path-injection</c>). Die
/// Implementierungen pruefen sie deshalb als letzten Riegel selbst, auch wenn die API sie
/// bereits an ihrem Eingang geprueft hat.
/// </para>
/// <para>
/// Ein bereits gespeicherter Stand wird nie ueberschrieben: <see cref="SaveVersion"/> meldet
/// eine schon vergebene Versionsnummer als Konflikt, statt den vorhandenen Stand still zu
/// ersetzen. Zwei gleichzeitige Uploads bekommen dadurch nicht beide dieselbe Nummer.
/// </para>
/// </remarks>
public interface IDecisionStorage
{
    /// <summary>Alle Katalogkoepfe, nach Kennung sortiert.</summary>
    Task<IReadOnlyList<DecisionDefinition>> GetDefinitions();

    /// <summary>Ein Katalogkopf, oder <c>null</c>, wenn es ihn nicht gibt.</summary>
    Task<DecisionDefinition?> GetDefinition(string decisionDefinitionId);

    /// <summary>Legt einen Katalogkopf an oder aktualisiert seinen Namen.</summary>
    Task SaveDefinition(DecisionDefinition definition);

    /// <summary>Entfernt einen Katalogkopf samt allen seinen Staenden.</summary>
    Task DeleteDefinition(string decisionDefinitionId);

    /// <summary>Die Staende einer Datei, aufsteigend nach Versionsnummer.</summary>
    Task<IReadOnlyList<DecisionDefinitionVersion>> GetVersions(string decisionDefinitionId);

    /// <summary>Ein bestimmter Stand, oder <c>null</c>, wenn es ihn nicht gibt.</summary>
    Task<DecisionDefinitionVersion?> GetVersion(string decisionDefinitionId, int version);

    /// <summary>
    /// Der juengste — und damit deployte — Stand einer Datei. Der Standard reicht fuer
    /// einfache Adapter; eine Datenbank darf ihn gezielter beantworten.
    /// </summary>
    async Task<DecisionDefinitionVersion?> GetLatestVersion(string decisionDefinitionId) =>
        (await GetVersions(decisionDefinitionId)).MaxBy(version => version.Version);

    /// <summary>
    /// Speichert einen neuen Stand.
    /// </summary>
    /// <exception cref="Exceptions.DefinitionStorageConflictException">
    /// Diese Versionsnummer ist fuer diese Datei bereits vergeben.
    /// </exception>
    Task SaveVersion(DecisionDefinitionVersion version);
}

/// <summary>Kompatibilitaetswache fuer Host-Adapter ohne Entscheidungsablage.</summary>
internal sealed class UnsupportedDecisionStorage : IDecisionStorage
{
    public static UnsupportedDecisionStorage Instance { get; } = new();
    private UnsupportedDecisionStorage() { }

    public Task<IReadOnlyList<DecisionDefinition>> GetDefinitions() => Unsupported<IReadOnlyList<DecisionDefinition>>();
    public Task<DecisionDefinition?> GetDefinition(string decisionDefinitionId) => Unsupported<DecisionDefinition?>();
    public Task SaveDefinition(DecisionDefinition definition) => Unsupported<bool>();
    public Task DeleteDefinition(string decisionDefinitionId) => Unsupported<bool>();
    public Task<IReadOnlyList<DecisionDefinitionVersion>> GetVersions(string decisionDefinitionId) =>
        Unsupported<IReadOnlyList<DecisionDefinitionVersion>>();
    public Task<DecisionDefinitionVersion?> GetVersion(string decisionDefinitionId, int version) =>
        Unsupported<DecisionDefinitionVersion?>();
    public Task<DecisionDefinitionVersion?> GetLatestVersion(string decisionDefinitionId) =>
        Unsupported<DecisionDefinitionVersion?>();
    public Task SaveVersion(DecisionDefinitionVersion version) => Unsupported<bool>();

    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support decision definitions."));
}
