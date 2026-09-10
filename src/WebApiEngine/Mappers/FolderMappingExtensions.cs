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
    public const string ReferenceModeText = "text";
    public const string ReferenceModeDirectory = "directory";
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
            ReferenceMode = ToWire(assignment.AssignmentMode),
            SubjectKind = ToWire(assignment.SubjectKind),
            Subject = assignment.Subject,
            Role = ToWire(assignment.Role),
            DisplayName = assignment.DisplayName,
            SubjectRef = ToDto(assignment.DirectorySubject)
        };
    }

    public static InheritedFolderAssignmentDto ToInheritedDto(this FolderAssignment assignment, WorkflowFolder source)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(source);

        return new InheritedFolderAssignmentDto
        {
            ReferenceMode = ToWire(assignment.AssignmentMode),
            SubjectKind = ToWire(assignment.SubjectKind),
            Subject = assignment.Subject,
            Role = ToWire(assignment.Role),
            DisplayName = assignment.DisplayName,
            SubjectRef = ToDto(assignment.DirectorySubject),
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

        var mode = ParseReferenceMode(dto.ReferenceMode);
        if (string.IsNullOrWhiteSpace(dto.Subject))
        {
            throw new ArgumentException("Eine Zuweisung braucht eine Kennung.", nameof(dto));
        }

        var subjectKind = ParseSubjectKind(dto.SubjectKind);
        SubjectRef? directorySubject = null;
        if (mode == FolderAssignmentMode.Text)
        {
            if (dto.SubjectRef is not null)
                throw new ArgumentException("Eine Freitextzuweisung darf keine Directory-Referenz enthalten.", nameof(dto));
        }
        else
        {
            directorySubject = ParseSubjectRef(dto.SubjectRef);
            var expectedKind = directorySubject.Kind == DirectorySubjectKind.User
                ? FolderSubjectKind.User
                : FolderSubjectKind.Group;
            if (subjectKind != expectedKind
                || !Guid.TryParse(dto.Subject, out var compatibleId)
                || compatibleId != directorySubject.Id)
            {
                throw new ArgumentException("Art, Kennung und Directory-Referenz einer Zuweisung muessen uebereinstimmen.", nameof(dto));
            }
        }

        return new FolderAssignment
        {
            AssignmentMode = mode,
            SubjectKind = subjectKind,
            Subject = mode == FolderAssignmentMode.Directory ? directorySubject!.Id.ToString() : dto.Subject.Trim(),
            Role = ParseRole(dto.Role),
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? null : dto.DisplayName.Trim(),
            DirectorySubject = directorySubject
        };
    }

    private static SubjectRef? ToSubjectRef(SubjectRefDto? dto) => dto is null
        ? null
        : new SubjectRef(ParseDirectorySubjectKind(dto.Kind), dto.Id);

    private static SubjectRefDto? ToDto(SubjectRef? subject) => subject is null
        ? null
        : new SubjectRefDto
        {
            Kind = subject.Kind == DirectorySubjectKind.User ? SubjectKindUser : SubjectKindGroup,
            Id = subject.Id
        };

    private static SubjectRef ParseSubjectRef(SubjectRefDto? dto)
    {
        var subject = ToSubjectRef(dto)
            ?? throw new ArgumentException("Eine Directory-Zuweisung braucht eine stabile Referenz.", nameof(dto));
        if (subject.Id == Guid.Empty)
            throw new ArgumentException("Eine Directory-Zuweisung braucht eine gueltige stabile Referenz.", nameof(dto));
        return subject;
    }

    public static string ToWire(FolderAssignmentMode mode) => mode switch
    {
        FolderAssignmentMode.Text => ReferenceModeText,
        FolderAssignmentMode.Directory => ReferenceModeDirectory,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

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

    public static FolderAssignmentMode ParseReferenceMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or ReferenceModeText => FolderAssignmentMode.Text,
            ReferenceModeDirectory => FolderAssignmentMode.Directory,
            _ => throw new ArgumentException($"\"{value}\" ist kein gueltiger Referenzmodus; erlaubt sind \"{ReferenceModeText}\" und \"{ReferenceModeDirectory}\".")
        };

    private static DirectorySubjectKind ParseDirectorySubjectKind(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            SubjectKindUser => DirectorySubjectKind.User,
            SubjectKindGroup => DirectorySubjectKind.Group,
            _ => throw new ArgumentException($"\"{value}\" ist keine gueltige Art einer Directory-Referenz.")
        };

    public static FolderRole ParseRole(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            RoleEditor => FolderRole.Editor,
            RoleSteward => FolderRole.Steward,
            _ => throw new ArgumentException($"\"{value}\" ist keine gueltige Rolle; erlaubt sind \"{RoleEditor}\" und \"{RoleSteward}\".")
        };
}
