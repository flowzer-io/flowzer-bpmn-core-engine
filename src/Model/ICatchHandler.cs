using System.Collections;

namespace Model;

public interface ICatchHandler
{
    List<DateTime> ActiveTimers { get; }
    List<TimerSubscriptionDescriptor> ActiveTimerSubscriptions { get; }
    List<MessageDefinition> ActiveCatchMessages { get; }
    List<string> ActiveCatchSignals { get;  }
    List<Token> ActiveUserTasks();

    /// <summary>
    /// Wartende Service-Tasks. Sie werden zu Auftraegen fuer externe Worker; die Engine
    /// fuehrt sie nicht selbst aus.
    /// </summary>
    List<Token> ActiveServiceTasks();
}
