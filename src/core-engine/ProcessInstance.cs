using BPMN.Process;
using FlowzerBPMN;

namespace core_engine;

public class ProcessInstance : ICatchHandler
{
     /// <summary>
     /// Eindeutige Id der Instanz
     /// </summary>
     public required Guid Id { get; set; }
     
     /// <summary>
     /// Das Model, das den BPMN-Prozess beschreibt
     /// </summary>
     public required Process ProcessModel { get; set; }

     /// <summary>
     /// Variablen des Prozesses
     /// </summary>
     public Dictionary<string, object> ProcessVariables { get; set; } = new();
     
     /// <summary>
     /// Aktuelle Tokens
     /// </summary>
     public List<Token> Tokens { get; init; } = new();

     
     
     Task<IEnumerable<Escalation>> GetActiveEscalations()
     {
         throw new NotImplementedException();
     }
     
     Task<IEnumerable<ActivityInfo>> GetActiveActivity()
     {
         throw new NotImplementedException();
     }

     Task<IEnumerable<MessageInfo>> GetActiveThrowMessages()
     {
         throw new NotImplementedException();
     }

     Task<IEnumerable<SignalInfo>> GetActiveThrowSignals()
     {
         throw new NotImplementedException();
     }

     
     public void HandleEscalation(string escalationCode, string? code, object? escalationBody = null)
     {
         throw new NotImplementedException();
     }
     
     public void HandleError(string name, string errorCode, string? errorMessage = null, object? errorBody = null)
     {
         throw new NotImplementedException();
     }
     
     /// <summary>
     /// Abbruch der Instanz
     /// </summary>
     public void Cancel()
     {
         throw new NotImplementedException();
     }

     public List<TimerDefinition> GetActiveTimers()
     {
         throw new NotImplementedException();
     }

     public List<MessageDefinition> GetActiveCatchMessages()
     {
         throw new NotImplementedException();
     }

     public List<SignalDefinition> GetActiveCatchSignals()
     {
         throw new NotImplementedException();
     }

     public Task<ProcessInstance> HandleTime(DateTime time)
     {
         throw new NotImplementedException();
     }

     public Task<ProcessInstance> HandleMessage(string messageName, string? correlationKey = null, object? messageData = null)
     {
         throw new NotImplementedException();
     }

     public Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null)
     {
         throw new NotImplementedException();
     }
}