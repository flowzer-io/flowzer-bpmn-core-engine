namespace Model;

public interface ICatchHandler
{
    List<DateTime> ActiveTimers { get; }
    List<MessageDefinition> ActiveCatchMessages { get; }
    List<SignalDefinition> ActiveCatchSignals { get;  }
    
}