using AutoMapper;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class FormController(
    IStorageSystem storageSystem,
    IMapper mapper): ControllerBase
{

    [HttpPost("{formId}")]
    public async Task<ActionResult<ApiStatusResult>> SaveForm(Guid formId, FormDto form)
    {
        if (formId == Guid.Empty)
            throw new Exception("FormId is required");

        form.FormId = formId;
        
        await storageSystem.FormStorage.SaveForm(mapper.Map<Form>(form));
        return Ok(new ApiStatusResult(){Successful = true});
    }

    [HttpGet("{formId}")]
    public async Task<ActionResult<ApiStatusResult<FormDto>>> GetForm(Guid formId)
    {
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
        public async Task<ActionResult<ApiStatusResult<FormMetaDataDto[]>>> GetFormMetadatas()
        {
            var formMetaData = await storageSystem.FormStorage.GetFormMetadatas();
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