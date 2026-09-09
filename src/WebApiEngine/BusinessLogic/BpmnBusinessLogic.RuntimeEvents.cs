using System.Security.Cryptography;
using System.Text;
using Model;

namespace WebApiEngine.BusinessLogic;

public partial class BpmnBusinessLogic
{
    /// <summary>
    /// Schreibt den an dieser Persistenzgrenze tatsächlich sichtbaren Zustand jedes
    /// Flow-Node-Tokens als append-only Fakt. Kurzlebige interne Zwischenzustände werden
    /// bewusst nicht erfunden. Die aus Tokenzustand und dessen persistiertem Zeitstempel
    /// abgeleitete ID macht einen Retry nach unklarem Commit-Ausgang idempotent.
    /// </summary>
    private static async Task SaveRuntimeNodeEvents(
        ITransactionalStorage storage,
        InstanceEngine instance,
        Guid definitionId)
    {
        var correlationId = Guid.NewGuid();
        try
        {
            foreach (var token in instance.Tokens
                         .Where(token => token.CurrentFlowNode is not null)
                         .OrderBy(token => token.LastStateChangeTime)
                         .ThenBy(token => token.Id))
            {
                var occurredAtUtc = RuntimeEventUtc(token.LastStateChangeTime);
                var runtimeEvent = new RuntimeNodeEvent
                {
                    Id = CreateRuntimeEventId(
                        instance.InstanceId,
                        definitionId,
                        token.Id,
                        token.CurrentFlowNode!.Id,
                        token.State,
                        occurredAtUtc),
                    ProcessInstanceId = instance.InstanceId,
                    DefinitionId = definitionId,
                    TokenId = token.Id,
                    FlowNodeId = token.CurrentFlowNode.Id,
                    State = token.State,
                    CorrelationId = correlationId,
                    OccurredAtUtc = occurredAtUtc
                };
                await storage.RuntimeNodeEventStorage.AppendIfAbsent(runtimeEvent);
            }
        }
        catch (NotSupportedException)
        {
            // Kompatibilitätsgrenze für externe/ältere Storage-Adapter. Die mitgelieferten
            // Datei- und PostgreSQL-Adapter unterstützen den Vertrag vollständig.
        }
    }

    private static DateTimeOffset RuntimeEventUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utc);
    }

    private static Guid CreateRuntimeEventId(
        Guid processInstanceId,
        Guid definitionId,
        Guid tokenId,
        string flowNodeId,
        FlowNodeState state,
        DateTimeOffset occurredAtUtc)
    {
        var source = string.Join('|',
            processInstanceId.ToString("N"),
            definitionId.ToString("N"),
            tokenId.ToString("N"),
            flowNodeId,
            ((int)state).ToString(System.Globalization.CultureInfo.InvariantCulture),
            occurredAtUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return new Guid(hash.AsSpan(0, 16));
    }
}
