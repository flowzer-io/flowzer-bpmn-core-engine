using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

/// <summary>Privater Bearbeitungsstand des aktuellen Benutzers fuer eine offene Aufgabe.</summary>
public sealed class UserTaskDraftDto
{
    public required Guid UserTaskId { get; init; }

    /// <summary>0 bedeutet, dass noch kein Entwurf gespeichert ist.</summary>
    public required long Revision { get; init; }

    public DateTimeOffset? UpdatedAtUtc { get; init; }

    [JsonConverter(typeof(ExpandoObjectConverter))]
    public required ExpandoObject Data { get; init; }
}

/// <summary>Vollstaendiger neuer Entwurfsstand samt erwarteter Serverrevision.</summary>
public sealed class SaveUserTaskDraftRequestDto
{
    public required long ExpectedRevision { get; init; }

    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Data { get; init; }
}
