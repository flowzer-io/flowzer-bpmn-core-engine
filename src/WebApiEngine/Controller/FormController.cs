using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using WebApiEngine.Auth;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class FormController(
    IStorageSystem storageSystem,
    FormBusinessLogic formBusinessLogic,
    BpmnBusinessLogic bpmnBusinessLogic,
    ICurrentUserContextAccessor currentUserContextAccessor): ControllerBase
{

    [HttpPost()]
    public async Task<ActionResult<ApiStatusResult>> SaveForm(FormDto formDto)
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
            throw new Exception("FormId is required");
        
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
        public async Task<ActionResult<ApiStatusResult>> SaveFormMetadata(Guid formId, FormMetaDataDto formMetadataDto)
        {
            if (formMetadataDto.FormId == Guid.Empty)
                throw new Exception("FormId is required");
            
            formMetadataDto.FormId = formId;
            
            await storageSystem.FormStorage.SaveFormMetaData(formMetadataDto.ToModel());
            return Ok(new ApiStatusResult(){Successful = true});
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
            await bpmnBusinessLogic.HandleUserTask(data, currentUser.UserId);
            return Ok(new ApiStatusResult() {Successful = true});
        }
        catch (Exception e)
        {
            return BadRequest(new ApiStatusResult(e.Message));
        }
    }

    #endregion
    

}
