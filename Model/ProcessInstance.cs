using BPMN.Process;

namespace Model;

public class ProcessInstance : ICatchHandler
{
    
    /// <summary>
    /// Eindeutige Id der Instanz
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    ///  ID eines übergeordneten Prozesses
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Das Model, das den BPMN-Prozess beschreibt
    /// </summary>
    public required Process ProcessModel { get; set; }

    /// <summary>
    /// Variablen des Prozesses
    /// </summary>
    public Variables ProcessVariables { get; set; } = new();

    /// <summary>
    /// Aktuelle Tokens
    /// </summary>
    public List<Token> Tokens { get; init; } = [];
    
    /// <summary>
    /// Status der Instanz
    /// </summary>
    public ProcessInstanceState State { set; get;}
    
    public List<DateTime> ActiveTimers { get; set; } = [];
    public List<MessageDefinition> ActiveCatchMessages { get; set; } = [];
    public List<SignalDefinition> ActiveCatchSignals { get; set; } = [];
    public Dictionary<string, object> ContextData { get; set; } = new();
}