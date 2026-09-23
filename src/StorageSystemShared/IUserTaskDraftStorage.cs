namespace StorageSystem;

/// <summary>Atomarer Compare-and-swap-Vertrag fuer private Aufgabenentwuerfe.</summary>
public interface IUserTaskDraftStorage
{
    Task<UserTaskDraft?> Get(Guid userTaskId, string ownerKey);

    /// <summary>
    /// Schreibt nur, wenn die aktuelle Revision <paramref name="expectedRevision"/> entspricht.
    /// Revision 0 bezeichnet einen noch nicht vorhandenen Entwurf.
    /// </summary>
    Task<UserTaskDraftWriteResult> TrySave(UserTaskDraft draft, long expectedRevision);

    /// <summary>Loescht nur bei passender Revision; ein fehlender Entwurf entspricht Revision 0.</summary>
    Task<UserTaskDraftDeleteResult> TryDelete(Guid userTaskId, string ownerKey, long expectedRevision);

    /// <summary>
    /// Zaehlt die Entwuerfe aller Eigentuemer einer Aufgabe. Die Instanzmigration kuendigt damit
    /// vorab an, dass private Arbeit verworfen wird; der Inhalt bleibt dabei ungelesen.
    ///
    /// Bewusst ohne stillen Standard, wie beim Loeschen einer Instanz: Eine Ablage, die
    /// Entwuerfe fuehrt, aber diesen Vertrag nicht kennt, meldete sonst glaubhaft „keine".
    /// </summary>
    Task<int> CountForTask(Guid userTaskId) =>
        throw new NotSupportedException($"{GetType().Name} unterstuetzt das Zaehlen von Entwuerfen nicht.");

    /// <summary>
    /// Entfernt alle Entwuerfe einer Aufgabe und liefert deren Anzahl. Wird gebraucht, wenn die
    /// Aufgabe nach einer Migration ein anderes Formular traegt: Ein Entwurf zum alten Schema
    /// waere dort nicht mehr einspielbar.
    /// </summary>
    Task<int> DeleteAllForTask(Guid userTaskId) =>
        throw new NotSupportedException($"{GetType().Name} unterstuetzt das Loeschen aller Entwuerfe nicht.");

    /// <summary>
    /// Bindet alle Entwuerfe einer Aufgabe an <paramref name="definitionId"/> und liefert deren
    /// Anzahl. Die Revision bleibt unveraendert: Der Umzug auf eine andere Workflow-Version ist
    /// keine Eingabe des Bearbeiters und darf seinen offenen Stand nicht zum Konflikt machen.
    /// </summary>
    Task<int> RebindAllForTask(Guid userTaskId, Guid definitionId) =>
        throw new NotSupportedException($"{GetType().Name} unterstuetzt das Umbinden von Entwuerfen nicht.");
}

/// <summary>
/// Gemeinsame Umbindung eines Entwurfs auf eine andere Workflow-Version. Beide Ablagen
/// schreiben denselben Rumpf; ein Entwurf muss zwischen ihnen unveraendert wandern koennen.
/// </summary>
public static class UserTaskDraftRebinding
{
    public static UserTaskDraft To(UserTaskDraft draft, Guid definitionId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new UserTaskDraft
        {
            UserTaskId = draft.UserTaskId,
            OwnerKey = draft.OwnerKey,
            OwnerUserId = draft.OwnerUserId,
            TokenId = draft.TokenId,
            ProcessInstanceId = draft.ProcessInstanceId,
            DefinitionId = definitionId,
            Revision = draft.Revision,
            UpdatedAtUtc = draft.UpdatedAtUtc,
            DataJson = draft.DataJson
        };
    }
}

public enum UserTaskDraftWriteStatus
{
    Written,
    RevisionConflict,
    TaskNotFound
}

public sealed record UserTaskDraftWriteResult(
    UserTaskDraftWriteStatus Status,
    UserTaskDraft? Draft,
    long CurrentRevision);

public enum UserTaskDraftDeleteStatus
{
    Deleted,
    RevisionConflict,
    TaskNotFound
}

public sealed record UserTaskDraftDeleteResult(
    UserTaskDraftDeleteStatus Status,
    long CurrentRevision);
