using System.Dynamic;
using Model;
using Newtonsoft.Json.Linq;

namespace FilesystemStorageSystem;

/// <summary>
/// Nicht polymorphes Dateiformat fuer Worker-Auftraege. Der Runtime-Token wird nicht
/// dupliziert: Fuer Claim und Abschluss werden nur seine stabilen Kennungen benoetigt.
/// </summary>
internal sealed class ServiceTaskJobDocument
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid TokenId { get; set; }
    public string FlowNodeId { get; set; } = string.Empty;
    public Guid ProcessInstanceId { get; set; }
    public string MetaDefinitionId { get; set; } = string.Empty;
    public Guid DefinitionId { get; set; }
    public string ProcessId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public int Retries { get; set; }
    public DateTime? RetryAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public ExpandoObject? Variables { get; set; }

    public static string Serialize(ServiceTaskJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Id == Guid.Empty || job.TokenId == Guid.Empty || string.IsNullOrWhiteSpace(job.FlowNodeId))
            throw new InvalidDataException("A service-task job requires stable job, token and flow-node IDs.");

        return SafeStorageJson.Serialize(new ServiceTaskJobDocument
        {
            Id = job.Id,
            Type = job.Type,
            Name = job.Name,
            TokenId = job.TokenId,
            FlowNodeId = job.FlowNodeId,
            ProcessInstanceId = job.ProcessInstanceId,
            MetaDefinitionId = job.MetaDefinitionId,
            DefinitionId = job.DefinitionId,
            ProcessId = job.ProcessId,
            CreatedAt = job.CreatedAt,
            LockedUntil = job.LockedUntil,
            LockedBy = job.LockedBy,
            Retries = job.Retries,
            RetryAt = job.RetryAt,
            LastErrorMessage = job.LastErrorMessage,
            Variables = job.Variables
        });
    }

    public static ServiceTaskJob Deserialize(string json)
    {
        var document = SafeStorageJson.Deserialize<ServiceTaskJobDocument>(json);
        if (document.TokenId == Guid.Empty || string.IsNullOrWhiteSpace(document.FlowNodeId))
        {
            // Vor diesem Format speicherte die Dateiablage den gesamten polymorphen Token.
            // JObject liest nur Daten; es instanziiert den dort genannten CLR-Typ nicht.
            var legacyToken = SafeStorageJson.ParseObject(json)["Token"];
            document.TokenId = legacyToken?["Id"]?.Value<Guid>() ?? Guid.Empty;
            document.FlowNodeId = legacyToken?["CurrentBaseElement"]?["Id"]?.Value<string>()
                                  ?? string.Empty;
        }

        if (document.Id == Guid.Empty || document.TokenId == Guid.Empty
            || document.ProcessInstanceId == Guid.Empty || string.IsNullOrWhiteSpace(document.FlowNodeId))
            throw new InvalidDataException("Stored service-task job has an invalid identity binding.");

        return new ServiceTaskJob
        {
            Id = document.Id,
            Type = document.Type,
            Name = document.Name,
            TokenId = document.TokenId,
            FlowNodeId = document.FlowNodeId,
            ProcessInstanceId = document.ProcessInstanceId,
            MetaDefinitionId = document.MetaDefinitionId,
            DefinitionId = document.DefinitionId,
            ProcessId = document.ProcessId,
            CreatedAt = document.CreatedAt,
            LockedUntil = document.LockedUntil,
            LockedBy = document.LockedBy,
            Retries = document.Retries,
            RetryAt = document.RetryAt,
            LastErrorMessage = document.LastErrorMessage,
            Variables = document.Variables
        };
    }
}
