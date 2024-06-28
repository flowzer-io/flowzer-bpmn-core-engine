using System.Text;
using AutoMapper;
using core_engine;
using Model;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class DefinitionController(IStorageSystem storageSystem, IMapper mapper ) : ControllerBase
{
    
    [HttpPost]
    public async Task<ActionResult<BpmnDefinitionDto>> UploadDefinition([FromQuery] Guid? previousGuid)
    {
        using var reader = new StreamReader(Request.Body, encoding: Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var rawContent = await reader.ReadToEndAsync();
        
        var model = ModelParser.ParseModel(rawContent);


        var highestVersion = await storageSystem.DefinitionStorage.GetMaxVersionId(model.Id);
        if (highestVersion == null)
            highestVersion = new BpmnVersion(1, 0);
        else
            highestVersion = highestVersion + 1;
                
        var definition = new BpmnDefinition()
        {
            Id = Guid.NewGuid(),
            DefinitionId = model.Id,
            PreviousGuid = previousGuid,
            Hash = rawContent.GetHashCode().ToString(),
            SavedByUser = Guid.Parse("D266F2B6-E96E-4D4A-9C20-C8E541394DF0"), // User.Claims["guid"] or something like that
            SavedOn = DateTime.UtcNow,
            Version = highestVersion
        };

        await storageSystem.DefinitionStorage.StoreDefinition(definition);
        await storageSystem.DefinitionStorage.StoreBinary(definition.Id, rawContent);
        
        return Ok(mapper.Map<BpmnDefinitionDto>(definition));
    }
    

    [HttpGet]
    public async Task<ActionResult<BpmnDefinitionDto[]>> GetAllDefinitions()
    {
        var allBinaryDefinitions = await storageSystem.DefinitionStorage.GetAllDefinitions();

        var bpmnDefinitionDto = mapper.Map<BpmnDefinitionDto[]>(allBinaryDefinitions);
        return Ok(bpmnDefinitionDto);
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<BpmnDefinitionDto>> GetDefinitionById([FromRoute] string id)
    {
        var allBinaryDefinitions = await storageSystem.DefinitionStorage.GetDefinitionById(id);
        var bpmnDefinitionDto = mapper.Map<BpmnDefinitionDto>(allBinaryDefinitions);
        return Ok(bpmnDefinitionDto);
    }

        
    [HttpGet("xml/{guid}")]
    public async Task<ActionResult<string>> GetDefinitionXml([FromRoute] Guid guid)
    {
        var xml = await storageSystem.DefinitionStorage.GetBinary(guid);
        return Ok(xml);
    }

    
    #region meta

    [HttpGet("meta")]
    public async Task<ActionResult<BpmnMetaDefinitionDto[]>> MetaIndex()
    {
        var allBinaryDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();

        var bpmnDefinitionDto = mapper.Map<BpmnMetaDefinitionDto[]>(allBinaryDefinitions);
        return Ok(bpmnDefinitionDto);
    }
    
    [HttpGet("meta/{id}")]
    public async Task<ActionResult<BpmnMetaDefinitionDto>> MetaGetById([FromRoute] string id)
    {
        var allBinaryDefinitions = await storageSystem.DefinitionStorage.GetMetaDefinitionById(id);
        var bpmnDefinitionDto = mapper.Map<BpmnMetaDefinitionDto>(allBinaryDefinitions);
        return Ok(bpmnDefinitionDto);
    }
    
        
    [HttpGet("meta/{id}/latest")]
    public async Task<ActionResult<BpmnDefinitionDto>> LatestDefinition([FromRoute] string id)
    {
        var latestDefinition = await storageSystem.DefinitionStorage.GetLatestDefinition(id);
        var bpmnDefinitionDto = mapper.Map<BpmnDefinitionDto>(latestDefinition);
        return Ok(bpmnDefinitionDto);
    }

    

    [HttpPost("meta")]
    public async Task<ActionResult<BpmnMetaDefinitionDto>> MetaPut(BpmnMetaDefinitionDto dto)
    {
        var definition = mapper.Map<BpmnMetaDefinition>(dto);
        await storageSystem.DefinitionStorage.StoreMetaDefinition(definition);
        return Ok(mapper.Map<BpmnMetaDefinitionDto>(definition));
    }
    

    #endregion
    
}