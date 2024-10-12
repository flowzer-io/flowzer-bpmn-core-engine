using System.Collections;

namespace Model;

public interface ICatchHandler
{
    List<DateTime> ActiveTimers { get; }
    List<MessageDefinition> ActiveCatchMessages { get; }
    List<string> ActiveCatchSignals { get;  }
    List<Token> ActiveUserTasks();
}