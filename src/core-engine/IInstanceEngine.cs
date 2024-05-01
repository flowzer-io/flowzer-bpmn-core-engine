using BPMN_Model.Process;

namespace core_engine;

public interface IInstanceEngine : ICatchHandler
{
     /// <summary>
     /// Eindeutige Id der Instanz
     /// </summary>
     Guid Id { get; set; }
     
     /// <summary>
     /// Der Prozess um den es sich handelt
     /// </summary>
     Process Process { get; set; }
     
     /// <summary>
     /// Variablen des Prozesses
     /// </summary>
     public Dictionary<string, object> ProcessVariables { get; set; }
     
//     
//     /// <summary>
//     /// Aktuelle Tokens
//     /// </summary>
//     public IEnumerable<Token> Tokens { get; init; }
//     
//     Task<IEnumerable<Escalation>> GetActiveEscalations();
//     Task<IEnumerable<ActivityInfo>> GetActiveActivity();
//     Task<IEnumerable<MessageInfo>> GetActiveThrowMessages();
//     Task<IEnumerable<SignalInfo>> GetActiveThrowSignals();
//     
//     
//     public void CompleteActivity(Token token, Activity activity, Dictionary<string, object> variables)
//     {
//         // Die ProcessActivity wird abgeschlossen und die Variablen werden gesetzt
//         
//         throw new NotImplementedException();
//     }
//
//     public void HandleMessage(string name, string? correlationKey = null, object? messageBody= null)
//     {
//         throw new NotImplementedException();
//     }
//     
//     public void HandleSignal(string name, object? signalBody = null)
//     {
//         throw new NotImplementedException();
//     }
//
//     public void HandleTime(DateTime time)
//     {
//         throw new NotImplementedException();
//     }
//     
//     public void HandleEscalation(string escalationCode, string? code, object? escalationBody = null)
//     {
//         throw new NotImplementedException();
//     }
//     
//     public void HandleError(string name, string errorCode, string? errorMessage = null, object? errorBody = null)
//     {
//         throw new NotImplementedException();
//     }
//     
//     /// <summary>
//     /// Abbruch der Instanz
//     /// </summary>
//     public void Cancel()
//     {
//         throw new NotImplementedException();
//     }
}