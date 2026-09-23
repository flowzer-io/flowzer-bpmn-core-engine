using Model;

namespace StorageSystem;

/// <summary>
/// Ablage der von außen aufrufbaren Auslöser.
///
/// Zählerstand und letzter Fehler haben eigene Methoden und gehören nicht in
/// <see cref="Save"/>: Sie werden bei jedem Aufruf geschrieben, während die Verwaltung
/// gleichzeitig Namen oder Felder ändern kann. Ein Lesen-Ändern-Schreiben über das ganze
/// Objekt würde die eine Änderung mit der anderen überschreiben, und zwei gleichzeitige
/// Aufrufe zählten nur einmal.
/// </summary>
public interface IInboundTriggerStorage
{
    Task<IReadOnlyList<InboundTrigger>> GetAll();

    Task<InboundTrigger?> Get(Guid id);

    /// <summary>
    /// Sucht den Auslöser zur öffentlichen Adresse. Liefert ihn auch, wenn er abgeschaltet ist:
    /// Erst der Aufrufer entscheidet, ob daraus eine Antwort oder eine Ablehnung wird, und beide
    /// müssen von außen ununterscheidbar sein.
    /// </summary>
    Task<InboundTrigger?> GetByKey(string key);

    /// <summary>
    /// Legt an oder aktualisiert die verwalteten Felder. Zähler und Fehlerfelder bleiben dabei
    /// unberührt; dafür gibt es <see cref="RecordUse"/> und <see cref="RecordFailure"/>.
    /// </summary>
    Task Save(InboundTrigger trigger);

    /// <summary>Liefert <c>false</c>, wenn es den Auslöser nicht (mehr) gibt.</summary>
    Task<bool> Remove(Guid id);

    /// <summary>
    /// Zählt einen erfolgreichen Aufruf. Muss atomar erhöhen, nicht gelesenen Stand plus eins
    /// zurückschreiben: Zwei gleichzeitige Aufrufe desselben Auslösers sind der Normalfall.
    /// </summary>
    Task RecordUse(Guid id, DateTime usedAt);

    /// <summary>
    /// Vermerkt eine Ablehnung. <paramref name="reason"/> ist ein kurzer, fester Grund ohne
    /// Daten des Aufrufers.
    /// </summary>
    Task RecordFailure(Guid id, DateTime failedAt, string reason);
}

/// <summary>
/// Wache für Ablagen, die den Vertrag nicht kennen. Bewusst ohne stillen Standard: Eine
/// Installation ohne Trigger-Ablage soll beim ersten Verwaltungsaufruf klar scheitern, statt
/// Auslöser anzunehmen, die niemand später wiederfindet.
/// </summary>
internal sealed class UnsupportedInboundTriggerStorage : IInboundTriggerStorage
{
    public static UnsupportedInboundTriggerStorage Instance { get; } = new();

    private UnsupportedInboundTriggerStorage() { }

    public Task<IReadOnlyList<InboundTrigger>> GetAll() => throw Unsupported();
    public Task<InboundTrigger?> Get(Guid id) => throw Unsupported();
    public Task<InboundTrigger?> GetByKey(string key) => throw Unsupported();
    public Task Save(InboundTrigger trigger) => throw Unsupported();
    public Task<bool> Remove(Guid id) => throw Unsupported();
    public Task RecordUse(Guid id, DateTime usedAt) => throw Unsupported();
    public Task RecordFailure(Guid id, DateTime failedAt, string reason) => throw Unsupported();

    private static NotSupportedException Unsupported() =>
        new("This storage implementation does not support inbound triggers.");
}
