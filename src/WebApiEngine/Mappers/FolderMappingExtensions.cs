using Model;
using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>
/// Abbildung zwischen Ordnermodell und DTOs. Die beiden Aufzaehlungen werden bewusst als
/// Kleinbuchstaben-Zeichenketten uebertragen: Eine spaetere dritte Rolle verschiebt so nicht die
/// Bedeutung bereits ausgelieferter Zahlen.
/// </summary>
public static class FolderMappingExtensions
{
    public const string SubjectKindUser = "user";
    public const string SubjectKindGroup = "group";
    public const string RoleEditor = "editor";
    public const string RoleSteward = "steward";

    public static WorkflowFolderDto ToDto(this WorkflowFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new WorkflowFolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            ParentId = folder.ParentId,
            Description = folder.Description,
            CreatedOn = folder.CreatedOn,
            Assignments = folder.Assignments.Select(ToDto).ToList()
        };
    }

    public static FolderAssignmentDto ToDto(this FolderAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new FolderAssignmentDto
        {
            SubjectKind = ToWire(assignment.SubjectKind),
            Subject = assignment.Subject,
            Role = ToWire(assignment.Role),
            DisplayName = assignment.DisplayName
        };
    }

    public static InheritedFolderAssignmentDto ToInheritedDto(this FolderAssignment assignment, WorkflowFolder source)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(source);

        return new InheritedFolderAssignmentDto
        {
            SubjectKind = ToWire(assignment.SubjectKind),
            Subject = assignment.Subject,
            Role = ToWire(assignment.Role),
            DisplayName = assignment.DisplayName,
            InheritedFromId = source.Id,
            InheritedFromName = source.Name
        };
    }

    /// <summary>
    /// Liest eine Zuweisung aus einer Anfrage. Unbekannte Werte werden abgelehnt statt auf einen
    /// Standard abgebildet: Aus einem Tippfehler in <c>role</c> darf keine stille
    /// Rechteveraenderung werden.
    /// </summary>
    public static FolderAssignment ToModel(this FolderAssignmentDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.Subject))
        {
            throw new ArgumentException("Eine Zuweisung braucht eine Kennung.", nameof(dto));
        }

        return new FolderAssignment
        {
            SubjectKind = ParseSubjectKind(dto.SubjectKind),
            Subject = dto.Subject.Trim(),
            Role = ParseRole(dto.Role),
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? null : dto.DisplayName.Trim()
        };
    }

    public static string ToWire(FolderSubjectKind kind) => kind switch
    {
        FolderSubjectKind.User => SubjectKindUser,
        FolderSubjectKind.Group => SubjectKindGroup,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string ToWire(FolderRole role) => role switch
    {
        FolderRole.Editor => RoleEditor,
        FolderRole.Steward => RoleSteward,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    public static FolderSubjectKind ParseSubjectKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            SubjectKindUser => FolderSubjectKind.User,
            SubjectKindGroup => FolderSubjectKind.Group,
            _ => throw new ArgumentException($"\"{value}\" ist keine gueltige Art einer Zuweisung; erlaubt sind \"{SubjectKindUser}\" und \"{SubjectKindGroup}\".")
        };

    public static FolderRole ParseRole(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            RoleEditor => FolderRole.Editor,
            RoleSteward => FolderRole.Steward,
            _ => throw new ArgumentException($"\"{value}\" ist keine gueltige Rolle; erlaubt sind \"{RoleEditor}\" und \"{RoleSteward}\".")
        };
}
