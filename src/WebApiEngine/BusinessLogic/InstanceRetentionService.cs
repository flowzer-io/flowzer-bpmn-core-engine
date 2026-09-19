using Model;
using StorageSystem;
using WebApiEngine.Background;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Loescht beendete Instanzen, deren Aufbewahrungsfrist abgelaufen ist.
///
/// <b>Laufende Instanzen werden nie angefasst.</b> Der Dienst liest ausschliesslich
/// <see cref="IInstanceStorage.GetAllFinishedInstances"/>, und jede einzelne Instanz wird vor dem
/// Loeschen unter der Engine-Sperre noch einmal frisch gelesen und erneut geprueft. Zwischen dem
/// Auswaehlen und dem Loeschen kann sonst eine Instanz durch eine nachgereichte Nachricht wieder
/// anlaufen — dann trifft der Lauf sie nicht mehr.
/// </summary>
public sealed class InstanceRetentionService(
    ITransactionalStorageProvider storageProvider,
    IStorageSystem storageSystem)
{
    /// <summary>
    /// Ein Lauf: faellige Instanzen suchen und bis zur Stapelgrenze loeschen.
    /// </summary>
    /// <param name="globalDays">
    /// Installationsweite Frist in Tagen. <c>null</c> oder <c>0</c> heisst „nur dort loeschen, wo
    /// ein Workflow ausdruecklich eine Frist gesetzt hat".
    /// </param>
    /// <param name="nowUtc">Der Bezugszeitpunkt; ueber die Uhr des Aufrufers steuerbar.</param>
    /// <param name="batchSize">Hoechstzahl der in diesem Lauf geloeschten Instanzen.</param>
    /// <returns>Anzahl tatsaechlich geloeschter Instanzen.</returns>
    public async Task<int> RunAsync(
        int? globalDays,
        DateTime nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));

        var retentionByDefinition = await LoadRetentionByDefinition();
        var finished = await storageSystem.InstanceStorage.GetAllFinishedInstances();

        var due = new List<Guid>();
        foreach (var instance in finished)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDue(instance, globalDays, retentionByDefinition, nowUtc)) due.Add(instance.InstanceId);
            if (due.Count >= batchSize) break;
        }

        var deleted = 0;
        foreach (var instanceId in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await DeleteIfStillDue(instanceId, globalDays, retentionByDefinition, nowUtc)) deleted++;
        }

        return deleted;
    }

    /// <summary>
    /// Die wirksame Frist einer Instanz: Das Workflow-Metadatum schlaegt den globalen Wert, in
    /// beide Richtungen — kuerzer wie laenger. <c>0</c> am Workflow heisst ausdruecklich „nie
    /// loeschen" und gewinnt auch gegen eine gesetzte globale Frist; <c>null</c> heisst „nicht
    /// entschieden" und faellt auf den globalen Wert zurueck.
    /// </summary>
    internal static int? ResolveEffectiveDays(int? globalDays, int? definitionDays)
    {
        var days = definitionDays ?? globalDays;
        return days is > 0 ? days : null;
    }

    private async Task<IReadOnlyDictionary<string, int?>> LoadRetentionByDefinition()
    {
        var metaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
        var result = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var metaDefinition in metaDefinitions)
            result[metaDefinition.DefinitionId] = metaDefinition.RetentionDays;
        return result;
    }

    private static bool IsDue(
        ProcessInstanceInfo instance,
        int? globalDays,
        IReadOnlyDictionary<string, int?> retentionByDefinition,
        DateTime nowUtc)
    {
        // Doppelter Boden zur Auswahlabfrage: Was nicht beendet ist, wird nie geloescht.
        if (!instance.IsFinished) return false;

        retentionByDefinition.TryGetValue(instance.metaDefinitionId, out var definitionDays);
        var effectiveDays = ResolveEffectiveDays(globalDays, definitionDays);
        if (effectiveDays is not { } days) return false;

        // Eine Instanz ohne datierbares Ende bleibt stehen. Sie mangels Zeitstempel sofort zu
        // loeschen waere die gefaehrlichere Auslegung; sie zu behalten faellt im Bestand auf.
        var finishedAtUtc = ProcessInstanceLifetime.GetFinishedAtUtc(instance);
        if (finishedAtUtc is not { } finishedAt) return false;

        return finishedAt <= nowUtc - TimeSpan.FromDays(days);
    }

    /// <summary>
    /// Loescht genau eine Instanz — je Instanz eine Transaktion. Ein Fehler an einer Instanz
    /// laesst damit die bereits geloeschten geloescht und die uebrigen unberuehrt, statt einen
    /// ganzen Stapel zurueckzurollen.
    ///
    /// Die Faelligkeit wird unter der Engine-Sperre und am frisch gelesenen Stand noch einmal
    /// geprueft. Nur so ist ausgeschlossen, dass eine zwischenzeitlich wieder angelaufene oder
    /// migrierte Instanz mit dem alten Befund geloescht wird.
    /// </summary>
    private async Task<bool> DeleteIfStillDue(
        Guid instanceId,
        int? globalDays,
        IReadOnlyDictionary<string, int?> retentionByDefinition,
        DateTime nowUtc)
    {
        using var transactionalStorage = storageProvider.GetTransactionalStorage();
        await transactionalStorage.InstanceStorage.LockForMutation(instanceId);

        ProcessInstanceInfo instance;
        try
        {
            instance = await transactionalStorage.InstanceStorage.GetProcessInstance(instanceId);
        }
        catch (FileNotFoundException)
        {
            // Zwischenzeitlich von Hand oder mit ihrem Workflow geloescht. Kein Fehler.
            return false;
        }

        if (!IsDue(instance, globalDays, retentionByDefinition, nowUtc)) return false;

        await InstancePurge.ExecuteAsync(transactionalStorage, instanceId);
        transactionalStorage.CommitChanges();
        return true;
    }
}
