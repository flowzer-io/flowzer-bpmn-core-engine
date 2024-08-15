namespace Model;

public interface ICatchHandler
{
    List<DateTime> ActiveTimers { get; }
    List<MessageDefinition> ActiveCatchMessages { get; }
    List<string> ActiveCatchSignals { get;  }
    
}