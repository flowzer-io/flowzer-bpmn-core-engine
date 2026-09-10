using BPMN.HumanInteraction;
using Model;
using Newtonsoft.Json.Linq;

namespace FilesystemStorageSystem;

/// <summary>
/// Nicht polymorphes Dateiformat fuer User-Task-Subscriptions. Der aktuelle Token bleibt
/// allein in der Instanz; die Subscription speichert nur ihre stabile Referenz und Zuweisung.
/// </summary>
internal sealed class UserTaskSubscriptionDocument
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid TokenId { get; set; }
    public string FlowNodeId { get; set; } = string.Empty;
    public Guid? ProcessInstanceId { get; set; }
    public string MetaDefinitionId { get; set; } = string.Empty;
    public Guid DefinitionId { get; set; }
    public string ProcessId { get; set; } = string.Empty;
    public List<Guid> UserCandidates { get; set; } = [];
    public List<Guid> UserGroups { get; set; } = [];
    public Guid? CurrenAssignedUser { get; set; }
    public string? Assignee { get; set; }
    public List<string> CandidateUsers { get; set; } = [];
    public List<string> CandidateGroups { get; set; } = [];
    public UserTaskAssignmentMode? AssignmentMode { get; set; }
    public Guid? DirectoryAssigneeUserId { get; set; }
    public List<Guid> DirectoryCandidateUserIds { get; set; } = [];
    public List<Guid> DirectoryCandidateGroupIds { get; set; } = [];

    public static string Serialize(UserTaskSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (subscription.Id == Guid.Empty || subscription.Token.Id == Guid.Empty
            || subscription.Token.CurrentFlowNode is not UserTask flowNode)
            throw new InvalidDataException("A user-task subscription requires stable task, token and flow-node IDs.");

        return SafeStorageJson.Serialize(new UserTaskSubscriptionDocument
        {
            Id = subscription.Id,
            Name = subscription.Name,
            TokenId = subscription.Token.Id,
            FlowNodeId = flowNode.Id,
            ProcessInstanceId = subscription.ProcessInstanceId,
            MetaDefinitionId = subscription.MetaDefinitionId,
            DefinitionId = subscription.DefinitionId,
            ProcessId = subscription.ProcessId,
            UserCandidates = [.. subscription.UserCandidates],
            UserGroups = [.. subscription.UserGroups],
            CurrenAssignedUser = subscription.CurrenAssignedUser,
            Assignee = subscription.Assignee,
            CandidateUsers = [.. subscription.CandidateUsers],
            CandidateGroups = [.. subscription.CandidateGroups],
            AssignmentMode = subscription.AssignmentMode,
            DirectoryAssigneeUserId = subscription.DirectoryAssigneeUserId,
            DirectoryCandidateUserIds = [.. subscription.DirectoryCandidateUserIds],
            DirectoryCandidateGroupIds = [.. subscription.DirectoryCandidateGroupIds]
        });
    }

    public static UserTaskSubscriptionDocument Deserialize(string json)
    {
        var document = SafeStorageJson.Deserialize<UserTaskSubscriptionDocument>(json);
        if (document.TokenId == Guid.Empty || string.IsNullOrWhiteSpace(document.FlowNodeId)
            || document.ProcessInstanceId is null)
        {
            // Sichere Vorwaertsmigration alter Dateien: Der alte Token wird als JSON-Baum
            // untersucht, niemals ueber sein $type-Feld instanziiert.
            var legacyToken = SafeStorageJson.ParseObject(json)["Token"];
            if (document.TokenId == Guid.Empty)
                document.TokenId = legacyToken?["Id"]?.Value<Guid>() ?? Guid.Empty;
            if (string.IsNullOrWhiteSpace(document.FlowNodeId))
                document.FlowNodeId = legacyToken?["CurrentBaseElement"]?["Id"]?.Value<string>()
                                      ?? string.Empty;
            document.ProcessInstanceId ??= legacyToken?["ProcessInstanceId"]?.Value<Guid>();
        }

        if (document.Id == Guid.Empty || document.TokenId == Guid.Empty
            || string.IsNullOrWhiteSpace(document.FlowNodeId))
            throw new InvalidDataException("Stored user-task subscription has an invalid identity binding.");

        return document;
    }

    public ExtendedUserTaskSubscription ToSubscription(Token token) => new()
    {
        Id = Id,
        Name = Name,
        Token = token,
        ProcessInstanceId = ProcessInstanceId,
        MetaDefinitionId = MetaDefinitionId,
        DefinitionId = DefinitionId,
        ProcessId = ProcessId,
        UserCandidates = [.. UserCandidates],
        UserGroups = [.. UserGroups],
        CurrenAssignedUser = CurrenAssignedUser,
        Assignee = Assignee,
        CandidateUsers = [.. CandidateUsers],
        CandidateGroups = [.. CandidateGroups],
        AssignmentMode = AssignmentMode,
        DirectoryAssigneeUserId = DirectoryAssigneeUserId,
        DirectoryCandidateUserIds = [.. DirectoryCandidateUserIds],
        DirectoryCandidateGroupIds = [.. DirectoryCandidateGroupIds]
    };
}
