
using WebApiEngine.Shared;

namespace FlowzerFrontend;

public class ColorHelper
{
    
    public static string GetStateTokenStateColor(ProcessInstanceStateDto? state)
    {
        if (state == null)
            return "lightgray";
        return state switch
        {
            ProcessInstanceStateDto.Running => "yellow",
            ProcessInstanceStateDto.Completed => "green",
            ProcessInstanceStateDto.Waiting => "orange",
            ProcessInstanceStateDto.Failed => "red",
            ProcessInstanceStateDto.Failing => "red",
            _ => "black"
        };
    }
}

