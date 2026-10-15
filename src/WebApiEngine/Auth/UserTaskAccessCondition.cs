using Microsoft.AspNetCore.Mvc;
using Model;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Auth;

/// <summary>
/// Optionale Einschränkungen eines Hostauftrags, niemals zusätzliche Rechte. Die Engine
/// prüft sie nach frischer Autorisierung innerhalb derselben Aufgaben-/Storage-Sperre wie
/// Lesen oder Schreiben. Ohne Parameter bleibt der bisherige Konsolenvertrag erhalten.
/// </summary>
public sealed class UserTaskAccessCondition
{
    [FromQuery(Name = "expectedProcessInstanceId")]
    public Guid? ExpectedProcessInstanceId { get; init; }
    [FromQuery(Name = "expectedDefinitionId")]
    public Guid? ExpectedDefinitionId { get; init; }
    [FromQuery(Name = "requireAssignedToCurrentUser")]
    public bool RequireAssignedToCurrentUser { get; init; }

    /// <summary>Kandidaten- oder Operatorrechte ersetzen keinen tatsächlich persönlichen Claim.</summary>
    internal bool Allows(UserTaskAccess access) => !RequireAssignedToCurrentUser
        || access.State?.AssigneeOwnerKey is not null && access.IsAssignedToCurrentUser && access.CanWork;

    /// <summary>Auch Guid.Empty ist ein erwarteter Wert, kein Wildcard. Fehler enthalten keine Zielkennungen.</summary>
    internal void EnsureBinding(UserTaskSubscription task)
    {
        if (ExpectedProcessInstanceId is { } instance && task.ProcessInstanceId != instance
            || ExpectedDefinitionId is { } definition && task.DefinitionId != definition)
            throw new UserTaskBindingConflictException();
    }
}
