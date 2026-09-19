using System.Collections.Concurrent;
using WebApiEngine.Shared;

namespace WebApiEngine.Connectors;

/// <summary>
/// Haelt Zaehler und letzten Zustand je mitgeliefertem Konnektor fuer die Betriebssicht.
///
/// Der letzte Fehler stammt immer aus einer vom Konnektor formulierten Meldung. Rohe
/// Ausnahmetexte und aufgeloeste Secrets gehoeren nicht hierher: Diese Werte gehen ueber
/// <c>GET /operations/diagnostics</c> hinaus.
/// </summary>
public sealed class ConnectorDiagnosticsState
{
    private readonly ConcurrentDictionary<string, ConnectorSnapshot> _connectors = new(StringComparer.Ordinal);

    public void MarkConfigured(string name, string jobType, bool enabled) =>
        _connectors.AddOrUpdate(
            name,
            _ => new ConnectorSnapshot(name, jobType) { Enabled = enabled },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Enabled = enabled;
                }

                return existing;
            });

    public void MarkRun(string name, DateTime atUtc) => Update(name, snapshot => snapshot.LastRunAtUtc = atUtc);

    public void MarkProcessed(string name) => Update(name, snapshot =>
    {
        snapshot.ProcessedJobs++;
        snapshot.LastErrorMessage = null;
    });

    public void MarkFailed(string name, string error) => Update(name, snapshot =>
    {
        snapshot.FailedJobs++;
        snapshot.LastErrorMessage = error;
    });

    public OperationsConnectorDto[] GetSnapshot() =>
        _connectors.Values
            .OrderBy(connector => connector.Name, StringComparer.Ordinal)
            .Select(connector =>
            {
                lock (connector)
                {
                    return new OperationsConnectorDto
                    {
                        Name = connector.Name,
                        JobType = connector.JobType,
                        Enabled = connector.Enabled,
                        LastRunAtUtc = connector.LastRunAtUtc,
                        ProcessedJobs = connector.ProcessedJobs,
                        FailedJobs = connector.FailedJobs,
                        LastErrorMessage = connector.LastErrorMessage
                    };
                }
            })
            .ToArray();

    private void Update(string name, Action<ConnectorSnapshot> change)
    {
        if (!_connectors.TryGetValue(name, out var snapshot))
        {
            return;
        }

        lock (snapshot)
        {
            change(snapshot);
        }
    }

    private sealed class ConnectorSnapshot(string name, string jobType)
    {
        public string Name { get; } = name;
        public string JobType { get; } = jobType;
        public bool Enabled { get; set; }
        public DateTime? LastRunAtUtc { get; set; }
        public long ProcessedJobs { get; set; }
        public long FailedJobs { get; set; }
        public string? LastErrorMessage { get; set; }
    }
}
