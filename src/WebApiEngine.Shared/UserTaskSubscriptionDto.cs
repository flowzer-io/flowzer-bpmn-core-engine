namespace WebApiEngine.Shared;

public class UserTaskSubscriptionDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required TokenDto Token { get; set; }
    public List<Guid> UserCandidates { get; set; } = new List<Guid>();
    public List<Guid> UserGroups { get; set; } = new List<Guid>();
    public Guid? CurrenAssignedUser { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    public required Guid DefinitionId { get; set; }
    public required string ProcessId { get; set; }
}

public class ExtendedUserTaskSubscriptionDto:UserTaskSubscriptionDto
{
    public string DefinitionMetaName { get; set; }
    public VersionDto DefinitionVersion { get; set; }
}