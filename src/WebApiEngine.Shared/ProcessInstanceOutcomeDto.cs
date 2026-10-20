using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

/// <summary>Datensparsame fachliche Projektion; Unknown bedeutet ausdrücklich keine Entscheidungsaussage.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProcessInstanceOutcomeDto>))]
public enum ProcessInstanceOutcomeDto
{
    /// <summary>Kein eindeutig belegbares Ergebnis aus einer reviewten, unveränderten Version.</summary>
    Unknown = 0,
    /// <summary>Die reviewte reguläre Genehmigungs-Endstelle wurde eindeutig abgeschlossen.</summary>
    Approved = 1,
    /// <summary>Die reviewte reguläre Ablehnungs-Endstelle wurde eindeutig abgeschlossen.</summary>
    Rejected = 2
}
