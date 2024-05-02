// namespace core_engine;
//
// public interface IEngine
// {
//     IEnumerable<IProcessEngine> Processes { get; }
//     IEnumerable<IInstanceEngine> Instances { get; }
//     
//     Task LoadModel(Stream model);
//     
//     Task<IEnumerable<TimerInfo>> GetActiveTimers();
//     Task<IEnumerable<MessageInfo>> GetActiveCatchMessages();
//     Task<IEnumerable<SignalInfo>> GetActiveCatchSignals();
//     Task<IEnumerable<ActivityInfo>> GetActiveActivity();
//     
//     Task SendTimeInfo(DateTime time);
//     Task ThrowMessage(string messageName, string? correlationKey = null, object? messageData = null);
//     Task ThrowSignal(string signalName, object? signalData = null);
//     Task ExecuteActivity(ActivityThrowInfo activity);
// }