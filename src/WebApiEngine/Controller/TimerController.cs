using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class TimerController(IStorageSystem storageSystem) : FlowzerControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<TimerSubscriptionDto[]>>> GetAllTimers()
    {
        var timers = (await storageSystem.SubscriptionStorage.GetAllTimerSubscriptions())
            .OrderBy(subscription => subscription.DueAt)
            .Select(subscription => subscription.ToDto())
            .ToArray();

        return Ok(new ApiStatusResult<TimerSubscriptionDto[]>(timers));
    }
}
