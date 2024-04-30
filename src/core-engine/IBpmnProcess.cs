using BPMN.Process;

namespace core_engine;

public interface IBpmnProcess
{
    Process Process { get; init; }
    
    Task<IEnumerable<TimerInfo>> GetActiveTimers();
    Task<IEnumerable<MessageInfo>> GetActiveCatchMessages();
    Task<IEnumerable<SignalInfo>> GetActiveCatchSignals();
    
    Task<BpmnInstance> SendTimeInfo(DateTime time);
    Task<BpmnInstance> ThrowMessage(string messageName, string? correlationKey = null, object? messageData = null);
    Task<BpmnInstance> ThrowSignal(string signalName, object? signalData = null);
    Task<BpmnInstance> StartProcess(ActivityThrowInfo activity);
}