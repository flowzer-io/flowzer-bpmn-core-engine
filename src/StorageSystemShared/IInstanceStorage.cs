using BPMN.Process;

namespace StorageSystem;

public interface IInstanceStorage
{
    public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId);
    Task AddInstance(ProcessInstanceInfo processInstanceInfo);
}

public record ProcessInstanceInfo (
    Guid Id,
    Guid DefinitionId,
    string ProcessId,
    List<Token> Tokens);