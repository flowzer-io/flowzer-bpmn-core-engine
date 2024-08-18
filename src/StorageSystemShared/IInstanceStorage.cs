using BPMN.Process;

namespace StorageSystem;

public interface IInstanceStorage
{
    public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId);
    Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo);
}

public record ProcessInstanceInfo (
    Guid InstanceId,
    string RelatedDefinitionId,
    Guid DefinitionId,
    string ProcessId,
    List<Token> Tokens);