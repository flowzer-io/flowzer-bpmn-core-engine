using AutoMapper;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class FormController(
    IStorageSystem storageSystem,
    IMapper mapper,
    FormBusinessLogic formBusinessLogic): ControllerBase
{

    [HttpPost()]
    public async Task<ActionResult<ApiStatusResult>> SaveForm(FormDto formDto)
    {
        
        var form = mapper.Map<Form>(formDto);
        
        if (form.FormId == Guid.Empty)
            throw new Exception("FormId is required");
        
        form = await formBusinessLogic.SaveForm(form);
        
        var retForm = mapper.Map<FormDto>(form);
        
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
            var versionFromFormIdentifier = Model.Version.FromString(formIdentifier);
            var foundVersion =  allVersions.SingleOrDefault(x => x.Version == versionFromFormIdentifier);
            if (foundVersion == null)
                return NotFound(new ApiStatusResult<FormDto>(){Successful = false, ErrorMessage = "Version not found"});
        }
        var formMetaData = await storageSystem.FormStorage.GetForm(formId);
        return Ok(new ApiStatusResult<FormDto>(mapper.Map<FormDto>(formMetaData)));
    }
    
    
    #region MetaData

        [HttpGet("meta/{formId}")]
        public async Task<ActionResult<ApiStatusResult<FormMetaDataDto>>> GetFormMetadata(Guid formId)
        {
            var formMetaData = await storageSystem.FormStorage.GetFormMetaData(formId);
            return Ok(new ApiStatusResult<FormMetaDataDto>(mapper.Map<FormMetaDataDto>(formMetaData)));
        }
        
        [HttpGet("meta")]
        public async Task<ActionResult<ApiStatusResult<FormMetaDataDto[]>>> GetFormMetadatas([FromQuery] string? search)
        {
            
            var formMetaData = await storageSystem.FormStorage.GetFormMetadatas();
            if (!string.IsNullOrEmpty(search))
                formMetaData = formMetaData.Where(x => string.Compare(x.Name,search, StringComparison.InvariantCultureIgnoreCase) == 0).ToList();
            return Ok(new ApiStatusResult<FormMetaDataDto[]>(mapper.Map<FormMetaDataDto[]>(formMetaData)));
        }
        
        [HttpPost("meta/{formId}")]
        public async Task<ActionResult<ApiStatusResult>> SaveFormMetadata(Guid formId, FormMetaDataDto formMetadataDto)
        {
            if (formMetadataDto.FormId == Guid.Empty)
                throw new Exception("FormId is required");
            
            formMetadataDto.FormId = formId;
            
            await storageSystem.FormStorage.SaveFormMetaData(mapper.Map<FormMetadata>(formMetadataDto));
            return Ok(new ApiStatusResult(){Successful = true});
        }

    #endregion
    

}