using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Auswertungen über die Laufzeithistorie. Liegt bewusst unter <c>/operations</c>: Die Zahlen
/// beschreiben fremde Vorgänge in ihrer Gesamtheit, das ist eine Betriebssicht. Sie bleibt
/// datensparsam — keine Instanzen, keine Akteure, keine Variablen.
/// </summary>
[ApiController, Route("operations")]
[Authorize(Policy = FlowzerPolicies.Operator)]
public sealed class OperationsAnalyticsController(WorkflowAnalyticsService analytics) : ControllerBase
{
    /// <summary>
    /// Kennzahlen aller Katalogeinträge im Zeitraum. Ohne Angabe gelten die letzten
    /// <see cref="WorkflowAnalyticsRange.DefaultDays"/> Tage.
    /// </summary>
    [HttpGet("analytics/workflows")]
    [ProducesResponseType<ApiStatusResult<WorkflowAnalyticsOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<WorkflowAnalyticsOverviewDto>>> GetWorkflowAnalytics(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to)
    {
        try
        {
            return Ok(new ApiStatusResult<WorkflowAnalyticsOverviewDto>(await analytics.GetOverviewAsync(from, to)));
        }
        catch (WorkflowAnalyticsRequestException exception)
        {
            return RangeProblem<WorkflowAnalyticsOverviewDto>(exception);
        }
    }

    /// <summary>
    /// Kennzahlen eines Katalogeintrags samt Schritten und Tagesverlauf. <c>definitionId</c>
    /// schränkt auf eine einzelne Version ein.
    /// </summary>
    [HttpGet("analytics/workflows/{metaDefinitionId}")]
    [ProducesResponseType<ApiStatusResult<WorkflowAnalyticsDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<WorkflowAnalyticsDetailDto>>> GetWorkflowAnalyticsDetail(
        string metaDefinitionId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? definitionId)
    {
        try
        {
            var detail = await analytics.GetDetailAsync(metaDefinitionId, from, to, definitionId);
            if (detail is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Workflow not found",
                    detail: "The workflow was not found.");
            }

            return Ok(new ApiStatusResult<WorkflowAnalyticsDetailDto>(detail));
        }
        catch (WorkflowAnalyticsRequestException exception)
        {
            return RangeProblem<WorkflowAnalyticsDetailDto>(exception);
        }
    }

    /// <summary>
    /// Die Anfrage ist wohlgeformt, aber nicht beantwortbar. Der Text nennt den Ausweg —
    /// einen kleineren Zeitraum —, damit die Oberfläche ihn weiterreichen kann.
    /// </summary>
    private ActionResult<ApiStatusResult<T>> RangeProblem<T>(WorkflowAnalyticsRequestException exception) =>
        Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "The analytics request could not be processed",
            detail: exception.Message);
}
