namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Der Auftrag gehoerte dem Worker nicht mehr, als der Abschluss unter der Instanzsperre
/// tatsaechlich geschrieben werden sollte. Ein zweiter API-Prozess hat ihn inzwischen
/// abgeschlossen, neu vergeben oder als gescheitert zurueckgestellt.
/// </summary>
public sealed class ServiceTaskLeaseLostException(Guid jobId)
    : InvalidOperationException($"The lease on service task job \"{jobId}\" was lost before the result was stored.")
{
    public Guid JobId { get; } = jobId;
}
