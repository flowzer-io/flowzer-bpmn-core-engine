using BPMN.Activities;
using BPMN.Common;
using BPMN.Events;
using BPMN.Process;

namespace core_engine;

public class BpmnInstance
{
    /// <summary>
    /// Id der Instanz
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Model des Prozesses
    /// </summary>
    public required BpmnModel Model { get; set; }
    
    /// <summary>
    /// Der Prozess innerhalb des Models, um den es sich handelt
    /// </summary>
    public required Process Process { get; set; }
    
    /// <summary>
    /// Aktuelle Tokens
    /// </summary>
    public List<Token> Tokens { get; init; } = [];
    
    /// <summary>
    /// Aktuelle CatchEvents, auf welche alle gehorcht wird.
    /// </summary>
    public List<CatchEvent> PossibleCatchEvents { get; init; } = [];
    
    /// <summary>
    /// 
    /// </summary>
    public List<ProcessFlowNode> ProcessActivities { get; init; } = [];
    
    /// <summary>
    /// 
    /// </summary>
    public Dictionary<string, object> ProcessVariables { get; set; } = new();
    
    /// <summary>
    /// Startet den Prozess "normal". Zum Starten durch ein Event siehe Handle-Methoden
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public void Start()
    {
        throw new NotImplementedException();
    }
    
    public void CompleteActivity(Token token, Activity activity, Dictionary<string, object> variables)
    {
        // Die ProcessActivity wird abgeschlossen und die Variablen werden gesetzt
        
        throw new NotImplementedException();
    }

    public void HandleMessage(string name, string? correlationKey = null, object? messageBody= null)
    {
        throw new NotImplementedException();
    }
    
    public void HandleSignal(string name, object? signalBody = null)
    {
        throw new NotImplementedException();
    }

    public void HandleTime(DateTime time)
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
}