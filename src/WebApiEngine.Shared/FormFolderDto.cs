namespace WebApiEngine.Shared;

public sealed class FormFolderDto
{
    public required Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public required string Name { get; init; }
}

public sealed class FormFolderRequestDto
{
    public Guid? ParentId { get; init; }
    public required string Name { get; init; }
}

public sealed class MoveFormRequestDto
{
    public Guid? FolderId { get; init; }
}

public sealed class FormVersionSummaryDto
{
    public required Guid Id { get; init; }
    public required Guid FormId { get; init; }
    public required VersionDto Version { get; init; }
}
