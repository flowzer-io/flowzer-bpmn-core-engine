namespace Model;

public class UserTaskSubscription
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required Token Token { get; set; }
    public List<Guid> UserCandidates { get; set; } = new List<Guid>();
    public List<Guid> UserGroups { get; set; } = new List<Guid>();
    public Guid? CurrenAssignedUser { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    
    public required string MetaDefinitionId { get; set; }
    public required Guid DefinitionId { get; set; }
    public required string ProcessId { get; set; }
    
}


public class ExtendedUserTaskSubscription: UserTaskSubscription
{
    public string DefinitionMetaName { get; set; }
    public Model.Version DefinitionVersion { get; set; }
}