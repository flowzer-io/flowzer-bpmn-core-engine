using Version = Model.Version;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Warum eine ganze Migrationsanfrage abgelehnt wird. Getrennt vom Ergebnis je Instanz: Diese
/// Faelle stehen fest, bevor irgendetwas geprueft oder veraendert wurde, und der Controller
/// bildet sie auf verschiedene HTTP-Statuscodes ab.
/// </summary>
public enum InstanceMigrationRequestStatus
{
    /// <summary>Die Anfrage ist zulaessig; das Ergebnis steht je Instanz.</summary>
    Accepted,

    NoInstances,
    TooManyInstances,
    DuplicateInstances,

    /// <summary>Mindestens eine der genannten Instanzen gibt es nicht.</summary>
    UnknownInstance,

    /// <summary>Die Instanzen gehoeren zu verschiedenen Workflows.</summary>
    MixedWorkflows,

    /// <summary>Die Instanzen laufen auf verschiedenen Quellversionen.</summary>
    MixedSourceVersions,

    /// <summary>Der Workflow hat keine deployte Version, auf die migriert werden koennte.</summary>
    NoDeployedVersion,

    /// <summary>Inzwischen ist eine andere Version deployt als die genannte Zielversion.</summary>
    TargetVersionChanged
}

/// <summary>Stabile Codes, die nicht aus der Engine stammen, sondern an dieser Schicht entstehen.</summary>
public static class InstanceMigrationCodes
{
    /// <summary>Die Instanz laeuft bereits auf der deployten Version.</summary>
    public const string AlreadyOnTargetVersion = "AlreadyOnTargetVersion";

    /// <summary>Der Umzug dieser Instanz ist unerwartet gescheitert; sie blieb unveraendert.</summary>
    public const string MigrationFailed = "MigrationFailed";

    /// <summary>
    /// Waehrend des Umzugs wurde eine andere Version deployt; diese Instanz blieb unveraendert.
    /// Die Engine-Sperre gilt nur im eigenen Prozess, ein zweiter API-Prozess kann dazwischen
    /// deployen — deshalb prueft jede Instanz die Zielversion in ihrer eigenen Transaktion neu.
    /// </summary>
    public const string TargetVersionChanged = "TargetVersionChanged";

    /// <summary>
    /// Die Ablage kann Aufgabenentwuerfe nicht mitziehen; die Instanz bleibt unveraendert,
    /// statt halb umgezogen liegen zu bleiben.
    /// </summary>
    public const string DraftStorageNotSupported = "DraftStorageNotSupported";

    /// <summary>Ein Timer am wartenden Knoten rechnet danach mit der Dauer der Zielversion.</summary>
    public const string TimerRecalculated = "TimerRecalculated";

    /// <summary>Die Aufgabe traegt in der Zielversion ein anderes Formular.</summary>
    public const string UserTaskFormChanged = "UserTaskFormChanged";

    /// <summary>Wegen des anderen Formulars wird mindestens ein privater Entwurf verworfen.</summary>
    public const string UserTaskDraftDiscarded = "UserTaskDraftDiscarded";

    /// <summary>Ein Worker arbeitet gerade an einem Auftrag dieser Instanz.</summary>
    public const string ServiceTaskJobInProgress = "ServiceTaskJobInProgress";
}

/// <summary>
/// Ein einzelner Befund. Der Code ist stabil und wird uebersetzt; die englische Meldung ist
/// technische Detailauskunft.
/// </summary>
public sealed record InstanceMigrationFinding(string Code, string? FlowNodeId, string Message);

public sealed record InstanceMigrationPreviewItem(
    Guid InstanceId,
    bool Migratable,
    IReadOnlyList<InstanceMigrationFinding> Problems,
    IReadOnlyList<InstanceMigrationFinding> Notices);

public sealed record InstanceMigrationResultItem(
    Guid InstanceId,
    bool Migrated,
    IReadOnlyList<InstanceMigrationFinding> Problems);

/// <summary>Das Ergebnis des Trockenlaufs. Veraendert nichts.</summary>
public sealed record InstanceMigrationPreview(
    InstanceMigrationRequestStatus Status,
    string? Message,
    string RelatedDefinitionId,
    Guid SourceDefinitionId,
    Version? SourceVersion,
    Guid TargetDefinitionId,
    Version? TargetVersion,
    IReadOnlyList<InstanceMigrationPreviewItem> Instances)
{
    public static InstanceMigrationPreview Rejected(InstanceMigrationRequestStatus status, string message) =>
        new(status, message, string.Empty, Guid.Empty, null, Guid.Empty, null, []);
}

/// <summary>Das Ergebnis des Umzugs, je Instanz einzeln.</summary>
public sealed record InstanceMigrationOutcome(
    InstanceMigrationRequestStatus Status,
    string? Message,
    Guid TargetDefinitionId,
    Version? TargetVersion,
    IReadOnlyList<InstanceMigrationResultItem> Instances)
{
    public static InstanceMigrationOutcome Rejected(InstanceMigrationRequestStatus status, string message) =>
        new(status, message, Guid.Empty, null, []);
}
