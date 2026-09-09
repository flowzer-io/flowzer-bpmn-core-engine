using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Middleware;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>Hostneutrale Modellierungs-API fuer wiederverwendbare Formularabschnitte.</summary>
[ApiController]
[Route("form-section")]
[Authorize(Policy = FlowzerPolicies.Modeler)]
public sealed class FormSectionController(FormSectionAuthoringService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiStatusResult<FormSectionMetadataDto[]>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiStatusResult<FormSectionMetadataDto[]>>> List() =>
        Ok(new ApiStatusResult<FormSectionMetadataDto[]>((await service.ListAsync()).ToArray()));

    [HttpGet("{sectionId:guid}")]
    [ProducesResponseType<ApiStatusResult<FormSectionMetadataDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<FormSectionMetadataDto>>> Get(Guid sectionId) =>
        Ok(new ApiStatusResult<FormSectionMetadataDto>(await service.GetMetadataAsync(sectionId)));

    [HttpPost]
    [ProducesResponseType<ApiStatusResult<FormSectionMetadataDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiStatusResult<FormSectionMetadataDto>>> Create(
        CreateFormSectionRequestDto request)
    {
        var created = await service.CreateAsync(request.Name);
        return CreatedAtAction(nameof(Get), new { sectionId = created.SectionId },
            new ApiStatusResult<FormSectionMetadataDto>(created));
    }

    [HttpPut("{sectionId:guid}")]
    [ProducesResponseType<ApiStatusResult<FormSectionMetadataDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<FormSectionMetadataDto>>> Rename(
        Guid sectionId,
        RenameFormSectionRequestDto request) =>
        Ok(new ApiStatusResult<FormSectionMetadataDto>(await service.RenameAsync(sectionId, request.Name)));

    [HttpGet("{sectionId:guid}/versions")]
    [ProducesResponseType<ApiStatusResult<FormSectionVersionSummaryDto[]>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<FormSectionVersionSummaryDto[]>>> ListVersions(Guid sectionId) =>
        Ok(new ApiStatusResult<FormSectionVersionSummaryDto[]>(
            (await service.ListVersionsAsync(sectionId)).ToArray()));

    [HttpGet("{sectionId:guid}/versions/{version}")]
    [ProducesResponseType<ApiStatusResult<FormSectionVersionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<FormSectionVersionDto>>> GetVersion(
        Guid sectionId,
        string version) =>
        Ok(new ApiStatusResult<FormSectionVersionDto>(await service.GetVersionAsync(sectionId, version)));

    [HttpGet("{sectionId:guid}/draft")]
    [ProducesResponseType<ApiStatusResult<FormSectionAuthoringDraftDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<FormSectionAuthoringDraftDto>>> GetDraft(Guid sectionId) =>
        Ok(new ApiStatusResult<FormSectionAuthoringDraftDto>(await service.GetDraftAsync(sectionId)));

    [HttpPut("{sectionId:guid}/draft")]
    [ProducesResponseType<ApiStatusResult<FormSectionAuthoringDraftDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<FormSectionAuthoringDraftDto>>> SaveDraft(
        Guid sectionId,
        SaveFormSectionAuthoringDraftRequestDto request) =>
        Ok(new ApiStatusResult<FormSectionAuthoringDraftDto>(await service.SaveDraftAsync(sectionId, request)));

    [HttpDelete("{sectionId:guid}/draft")]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult>> DeleteDraft(
        Guid sectionId,
        [FromQuery] long expectedRevision)
    {
        await service.DeleteDraftAsync(sectionId, expectedRevision);
        return Ok(new ApiStatusResult { Successful = true });
    }

    [HttpPost("{sectionId:guid}/publish")]
    [ProducesResponseType<ApiStatusResult<FormSectionVersionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<FormSectionVersionDto>>> Publish(
        Guid sectionId,
        PublishFormSectionAuthoringDraftRequestDto request) =>
        Ok(new ApiStatusResult<FormSectionVersionDto>(
            await service.PublishAsync(sectionId, request.ExpectedRevision)));
}
