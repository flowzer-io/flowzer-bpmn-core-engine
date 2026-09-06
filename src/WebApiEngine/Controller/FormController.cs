using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using WebApiEngine.Auth;
using Microsoft.AspNetCore.Authorization;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class FormController(
    IStorageSystem storageSystem,
    FormBusinessLogic formBusinessLogic,
    BpmnBusinessLogic bpmnBusinessLogic,
    ICurrentUserContextAccessor currentUserContextAccessor): ControllerBase
{

    [HttpPost()]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<FormDto>>> SaveForm(FormDto formDto)
    {
        Form form;
        try
        {
            form = formDto.ToModel();
        }
        catch (ArgumentException e)
        {
            return BadRequest(new ApiStatusResult<FormDto>(e.Message));
        }
        
        if (form.FormId == Guid.Empty)
            return BadRequest(new ApiStatusResult<FormDto>("FormId is required"));
        
        form = await formBusinessLogic.SaveForm(form);
        
        var retForm = form.ToDto();
        
        return Ok(new ApiStatusResult<FormDto>()
        {
            Result = retForm,
            Successful = true,
        });
    }

    [HttpGet("{formId}/{formIdentifier}")]
    public async Task<ActionResult<ApiStatusResult<FormDto>>> GetForm(Guid formId, string formIdentifier)
    {
        var allVersions = (await storageSystem.FormStorage.GetForms(formId)).ToList();
        if (formIdentifier == "latest")
        {
            if (allVersions.Count == 0)
                return NotFound(new ApiStatusResult<FormDto>(){Successful = false, ErrorMessage = "formular has no versions"});
            formId = allVersions.OrderByDescending(x => x.Version).First().Id;
        }
        else
        {
            Model.Version versionFromFormIdentifier;
            try
            {
                versionFromFormIdentifier = Model.Version.FromString(formIdentifier);
            }
            catch (ArgumentException e)
            {
                return BadRequest(new ApiStatusResult<FormDto>(e.Message));
            }

            // Der API-Pfad arbeitet mit der stabilen FormId, gespeichert werden die Versionen aber
            // unter ihrer konkreten Formular-Instanz-ID. Deshalb wird hier zuerst die Zielversion aufgelöst.
            var foundVersion =  allVersions.SingleOrDefault(x => x.Version.Equals(versionFromFormIdentifier));
            if (foundVersion == null)
                return NotFound(new ApiStatusResult<FormDto>(){Successful = false, ErrorMessage = "Version not found"});

            formId = foundVersion.Id;
        }

        var formMetaData = await storageSystem.FormStorage.GetForm(formId);
        return Ok(new ApiStatusResult<FormDto>(formMetaData.ToDto()));
    }
    
    
    #region MetaData

        [HttpGet("meta/{formId}")]
        public async Task<ActionResult<ApiStatusResult<FormMetaDataDto>>> GetFormMetadata(Guid formId)
        {
            var formMetaData = await storageSystem.FormStorage.GetFormMetaData(formId);
            return Ok(new ApiStatusResult<FormMetaDataDto>(formMetaData.ToDto()));
        }
        
        [HttpGet("meta")]
        public async Task<ActionResult<ApiStatusResult<FormMetaDataDto[]>>> GetFormMetadatas([FromQuery] string? search)
        {
            
            var formMetaData = await storageSystem.FormStorage.GetFormMetadatas();
            if (!string.IsNullOrEmpty(search))
                formMetaData = formMetaData.Where(x => string.Compare(x.Name,search, StringComparison.InvariantCultureIgnoreCase) == 0).ToList();
            return Ok(new ApiStatusResult<FormMetaDataDto[]>(formMetaData.Select(metadata => metadata.ToDto()).ToArray()));
        }
        
        [HttpPost("meta/{formId}")]
        [Authorize(Policy = FlowzerPolicies.Modeler)]
        public async Task<ActionResult<ApiStatusResult>> SaveFormMetadata(Guid formId, FormMetaDataDto formMetadataDto)
        {
            formMetadataDto.FormId = formId;
            
            await storageSystem.FormStorage.SaveFormMetaData(formMetadataDto.ToModel());
            return Ok(new ApiStatusResult(){Successful = true});
        }

        /// <summary>
        /// Entfernt ein Formular samt allen seinen Versionen.
        ///
        /// Benutzt ein deployter Workflow das Formular, bleibt es stehen: Ein Aufgabenformular
        /// wird ueber seinen <em>Namen</em> aufgeloest, ein geloeschtes liesse jede Aufgabe
        /// dieses Schrittes mit „No form named …" stehen — auch in bereits laufenden Instanzen.
        /// Der Aufrufer bekommt die betroffenen Workflows genannt, statt vor einem
        /// unerklaerlichen Fehlschlag zu stehen.
        /// </summary>
        [HttpDelete("meta/{formId:guid}")]
        [Authorize(Policy = FlowzerPolicies.Modeler)]
        [ProducesResponseType<ApiStatusResult<FormMetaDataDto>>(StatusCodes.Status200OK)]
        [ProducesResponseType<ApiStatusResult<FormMetaDataDto>>(StatusCodes.Status404NotFound)]
        [ProducesResponseType<ApiStatusResult<FormMetaDataDto>>(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ApiStatusResult<FormMetaDataDto>>> DeleteFormMetadata(Guid formId)
        {
            // Suchen, pruefen und loeschen liegen in der Engine unter derselben Sperre wie das
            // Deployen; hier auseinandergezogen koennte dazwischen deployt oder umbenannt werden.
            var ergebnis = await bpmnBusinessLogic.DeleteFormIfUnused(formId);

            if (ergebnis.Metadata is null)
            {
                return NotFound(new ApiStatusResult<FormMetaDataDto>(
                    errorMessage: $"Es gibt kein Formular mit der Kennung {formId}."));
            }

            if (ergebnis.UsedBy.Count > 0)
            {
                return Conflict(new ApiStatusResult<FormMetaDataDto>(
                    errorMessage: $"Das Formular \u201e{ergebnis.Metadata.Name}\u201c wird von "
                                  + $"{string.Join(", ", ergebnis.UsedBy)} benutzt. Erst dort ersetzen, dann loeschen."));
            }

            // Bewusst der geloeschte Eintrag: `new ApiStatusResult<string>(...)` traefe den
            // Konstruktor fuer die Fehlermeldung und meldete den Erfolg als Fehlschlag.
            return Ok(new ApiStatusResult<FormMetaDataDto>(ergebnis.Metadata.ToDto()));
        }

    #endregion


    #region MessageHandling

        
    [HttpPost("result")]
    public async Task<ActionResult<ApiStatusResult>> HandleUserFormData(UserTaskResultDto formMetadataDto)
    {
        try
        {
            var data = formMetadataDto.ToModel();
            var currentUser = currentUserContextAccessor.GetCurrentUser();
            var userId = currentUser.RequireResolvedUserId("submitting form results");
            await bpmnBusinessLogic.HandleUserTask(data, userId);
            return Ok(new ApiStatusResult() {Successful = true});
        }
        catch (ArgumentException e)
        {
            return BadRequest(new ApiStatusResult(e.Message));
        }
    }

    #endregion
    

}
