using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Bündelt lesbare Statusbezeichnungen und visuelle Tonklassen für Prozessinstanzen,
/// damit Listen- und Detailseiten dieselbe Sprache und Farbsemantik verwenden.
/// </summary>
public static class ProcessInstanceStateViewHelper
{
    public static string GetLabel(ProcessInstanceStateDto state)
    {
        return state switch
        {
            ProcessInstanceStateDto.Initialized => "Initialized",
            ProcessInstanceStateDto.Running => "Running",
            ProcessInstanceStateDto.Waiting => "Waiting",
            ProcessInstanceStateDto.Completing => "Completing",
            ProcessInstanceStateDto.Completed => "Completed",
            ProcessInstanceStateDto.Terminating => "Terminating",
            ProcessInstanceStateDto.Terminated => "Terminated",
            ProcessInstanceStateDto.Failing => "Failing",
            ProcessInstanceStateDto.Failed => "Failed",
            ProcessInstanceStateDto.Compensating => "Compensating",
            ProcessInstanceStateDto.Compensated => "Compensated",
            _ => state.ToString()
        };
    }

    public static string GetToneClass(ProcessInstanceStateDto state)
    {
        return state switch
        {
            ProcessInstanceStateDto.Completed or
            ProcessInstanceStateDto.Terminated or
            ProcessInstanceStateDto.Compensated => "status-pill-success",
            ProcessInstanceStateDto.Waiting or
            ProcessInstanceStateDto.Completing or
            ProcessInstanceStateDto.Terminating or
            ProcessInstanceStateDto.Compensating => "status-pill-warning",
            ProcessInstanceStateDto.Failing or
            ProcessInstanceStateDto.Failed => "status-pill-danger",
            _ => "status-pill-neutral"
        };
    }
}
