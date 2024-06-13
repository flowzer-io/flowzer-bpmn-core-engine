namespace Model;

public interface ICatchHandler
{
    List<DateTime> ActiveTimers { get; set; }
    List<MessageDefinition> ActiveCatchMessages { get; set; }
    List<SignalDefinition> ActiveCatchSignals { get; set; }
    
}