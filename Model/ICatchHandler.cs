namespace Model;

public interface ICatchHandler
{
    List<TimerEventDefinition> ActiveTimers { get; set; }
    List<MessageDefinition> ActiveCatchMessages { get; set; }
    List<SignalDefinition> ActiveCatchSignals { get; set; }
    
}