using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class HealthController(
    ITransactionalStorageProvider storageProvider,
    IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiStatusResult<HealthStatusDto>> GetLiveness()
    {
        var payload = new HealthStatusDto
        {
            Status = "Healthy",
            CheckedAtUtc = DateTime.UtcNow,
            Environment = environment.EnvironmentName,
            Storage = "NotChecked"
        };

        return Ok(new ApiStatusResult<HealthStatusDto>(payload));
    }

    [HttpGet("ready")]
    public async Task<ActionResult<ApiStatusResult<HealthStatusDto>>> GetReadiness()
    {
        try
        {
            using var storage = storageProvider.GetTransactionalStorage();
            _ = await storage.DefinitionStorage.GetAllDefinitions();

            var payload = new HealthStatusDto
            {
                Status = "Healthy",
                CheckedAtUtc = DateTime.UtcNow,
                Environment = environment.EnvironmentName,
                Storage = "Ready"
            };

            return Ok(new ApiStatusResult<HealthStatusDto>(payload));
        }
        catch (Exception exception)
        {
            var payload = new HealthStatusDto
            {
                Status = "Unhealthy",
                CheckedAtUtc = DateTime.UtcNow,
                Environment = environment.EnvironmentName,
                Storage = "Unavailable"
            };

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiStatusResult<HealthStatusDto>
            {
                Successful = false,
                ErrorMessage = $"Storage is unavailable: {exception.Message}",
                Result = payload
            });
        }
    }
}
