using Model;

namespace StorageSystem;

/// <summary>
/// Append-only Ablage datensparsamer Engine-Knotenereignisse. Die stabile Ereigniskennung macht
/// Wiederholungen nach Restart oder erneutem Persistieren idempotent.
/// </summary>
public interface IRuntimeNodeEventStorage
{
    /// <summary>
    /// Hängt das Ereignis genau einmal an. <c>true</c> bedeutet neu gespeichert, <c>false</c>
    /// bedeutet, dass dieselbe stabile Ereigniskennung bereits vorhanden war.
    /// </summary>
    Task<bool> AppendIfAbsent(RuntimeNodeEvent runtimeEvent);

    /// <summary>Liest die unveränderliche Ereignisspur einer Instanz in stabiler Zeitreihenfolge.</summary>
    Task<IReadOnlyList<RuntimeNodeEvent>> GetByProcessInstance(Guid processInstanceId);
}

/// <summary>Storage-unabhängige Schutzgrenzen des datensparsamen Runtime-Ereignisvertrags.</summary>
public static class RuntimeNodeEventContract
{
    public static void Validate(RuntimeNodeEvent runtimeEvent)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        if (runtimeEvent.Id == Guid.Empty || runtimeEvent.ProcessInstanceId == Guid.Empty
            || runtimeEvent.DefinitionId == Guid.Empty || runtimeEvent.TokenId == Guid.Empty
            || runtimeEvent.CorrelationId == Guid.Empty
            || string.IsNullOrWhiteSpace(runtimeEvent.FlowNodeId)
            || !Enum.IsDefined(runtimeEvent.State)
            || runtimeEvent.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The runtime node event is invalid.", nameof(runtimeEvent));
        }
    }
}

/// <summary>Kompatibilitätsadapter für Ablagen ohne Runtime-Ereignisspur.</summary>
internal sealed class UnsupportedRuntimeNodeEventStorage : IRuntimeNodeEventStorage
{
    internal static UnsupportedRuntimeNodeEventStorage Instance { get; } = new();
    private UnsupportedRuntimeNodeEventStorage() { }

    public Task<bool> AppendIfAbsent(RuntimeNodeEvent runtimeEvent) => Unsupported<bool>();
    public Task<IReadOnlyList<RuntimeNodeEvent>> GetByProcessInstance(Guid processInstanceId) =>
        Unsupported<IReadOnlyList<RuntimeNodeEvent>>();

    private static Task<T> Unsupported<T>() => Task.FromException<T>(
        new NotSupportedException("This storage adapter does not support runtime node events."));
}
