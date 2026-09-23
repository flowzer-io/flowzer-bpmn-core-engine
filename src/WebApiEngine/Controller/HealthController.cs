using Microsoft.AspNetCore.Authorization;
using PostgreSqlStorageSystem;
using WebApiEngine.Persistence;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Liveness/Readiness fuer Orchestratoren. Bewusst anonym, auch bei aktiver Authentifizierung.
/// </summary>
[ApiController, Route("[controller]"), AllowAnonymous]
public class HealthController(
    ITransactionalStorageProvider storageProvider,
    FlowzerStorageOptions storageOptions,
    IHostEnvironment environment,
    ILogger<HealthController> logger) : ControllerBase
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
    public async Task<ActionResult<ApiStatusResult<HealthStatusDto>>> GetReadiness(CancellationToken cancellationToken)
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
                Storage = "Ready",
                Details = await DescribeAsync(cancellationToken)
            };

            return Ok(new ApiStatusResult<HealthStatusDto>(payload));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Readiness probe failed because the storage backend is currently unavailable.");
            var payload = new HealthStatusDto
            {
                Status = "Unhealthy",
                CheckedAtUtc = DateTime.UtcNow,
                Environment = environment.EnvironmentName,
                Storage = "Unavailable",
                Details = new HealthReadinessDetailsDto
                {
                    StorageProvider = storageOptions.Describe(),
                    MigrationState = "Unknown",
                    ExpressionEngine = DescribeExpressionEngine()
                }
            };

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiStatusResult<HealthStatusDto>
            {
                Successful = false,
                ErrorMessage = "Storage is unavailable.",
                Result = payload
            });
        }
    }

    /// <summary>
    /// Welcher Ausdrucks-Handler tatsaechlich laeuft. Fehlt die native V8-Bibliothek der
    /// Plattform, baut <see cref="core_engine.FlowzerConfig"/> stillschweigend den einfachen
    /// Handler - die Installation laeuft dann ohne FEEL weiter. Ein Betreiber soll das sehen,
    /// ohne einen Prozess starten zu muessen.
    /// </summary>
    private static string DescribeExpressionEngine() =>
        core_engine.FlowzerConfig.Default.ExpressionHandler is core_engine.Expression.Feelin.FeelinExpressionHandler
            ? "Feel"
            : "Simple";

    /// <summary>
    /// Ergaenzt die Probe um den Migrationsstand. Bewusst nachgelagert und fehlertolerant: eine
    /// nicht lesbare Historie meldet <c>Unknown</c>, macht den Knoten aber nicht unbereit -
    /// die Ablage selbst hat oben bereits geantwortet.
    /// </summary>
    private async Task<HealthReadinessDetailsDto> DescribeAsync(CancellationToken cancellationToken)
    {
        var details = new HealthReadinessDetailsDto
        {
            StorageProvider = storageOptions.Describe(),
            MigrationState = "NotApplicable",
            ExpressionEngine = DescribeExpressionEngine()
        };

        if (!storageOptions.IsPostgreSql)
        {
            return details;
        }

        try
        {
            var status = await PostgreSqlMigrator.GetStatusAsync(
                storageOptions.PostgreSql.ConnectionString,
                storageOptions.PostgreSql.Schema,
                cancellationToken);
            details.ExpectedMigrationVersion = status.Available.LastOrDefault();
            details.PendingMigrationCount = status.Pending.Count;
            details.MigrationState = status.IsUpToDate ? "UpToDate" : "Pending";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Readiness probe could not read the migration history.");
            details.MigrationState = "Unknown";
            details.ExpectedMigrationVersion = PostgreSqlMigrator.AvailableVersions.LastOrDefault();
        }

        return details;
    }
}
