using AutoMapper;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class DefinitionController(
    IStorageSystem storageSystem,
    IMapper mapper,
    DefinitionBusinessLogic definitionBusinessLogic, BpmnLogic bpmnLogic) : FlowzerControllerBase
{
    
    [HttpPost]
    public async Task<ActionResult<BpmnDefinitionDto>> UploadDefinition([FromQuery] Guid? previousGuid)
    {
        var rawContent = await GetRawContent();
        var definition = await definitionBusinessLogic.StoreDefinition(rawContent, previousGuid);
        return Ok(mapper.Map<BpmnDefinitionDto>(definition));
    }
    
    [HttpPost("deploy")]
    public async Task<ActionResult<ApiStatusResult<BpmnDefinitionDto>>> DeployDefinition([FromQuery] Guid? previousGuid)
    {
        try
        {
            var rawContent = await GetRawContent();
            var definition = await definitionBusinessLogic.StoreDefinition(rawContent, previousGuid, true);
            await bpmnLogic.DeployDefinition(definition);
            return Ok(new ApiStatusResult<BpmnDefinitionDto>(mapper.Map<BpmnDefinitionDto>(definition)));
        }
        catch (Exception e)
        {
            return BadRequest(new ApiStatusResult<BpmnDefinitionDto>(e.Message));
        }
    }


    [HttpGet("new")]
    public async Task<ActionResult<BpmnMetaDefinitionDto>> NewDefinition()
    {
        
        var definitionId = "definition_" + Guid.NewGuid();
        var modelId = "model_" + Guid.NewGuid();
        var emptyXml = $"""
                        <?xml version="1.0" encoding="UTF-8"?>
                        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" id="{definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
                          <bpmn:process id="{modelId}" isExecutable="true" />
                          <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                            <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="{modelId}" />
                          </bpmndi:BPMNDiagram>
                        </bpmn:definitions>
                        """;
        
        await definitionBusinessLogic.StoreDefinition(emptyXml, null);
        var metaDefinition = new BpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "New Definition",
            IsActive = false
        };
        await storageSystem.DefinitionStorage.StoreMetaDefinition(metaDefinition);
        
        return Ok(mapper.Map<BpmnMetaDefinitionDto>(metaDefinition));
    }

    

    [HttpGet]
    public async Task<ActionResult<BpmnDefinitionDto[]>> GetAllDefinitions()
    {
        var allBinaryDefinitions = await storageSystem.DefinitionStorage.GetAllDefinitions();

        var bpmnDefinitionDto = mapper.Map<BpmnDefinitionDto[]>(allBinaryDefinitions);
        return Ok(bpmnDefinitionDto);
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<BpmnDefinitionDto>> GetDefinitionById([FromRoute] Guid id)
    {
        var definitionById = await storageSystem.DefinitionStorage.GetDefinitionById(id);
        var bpmnDefinitionDto = mapper.Map<BpmnDefinitionDto>(definitionById);
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
    public async Task<ActionResult<BpmnMetaDefinitionDto>> MetaPost([FromBody] BpmnMetaDefinitionDto dto)
    {
        var definition = mapper.Map<BpmnMetaDefinition>(dto);
        await storageSystem.DefinitionStorage.StoreMetaDefinition(definition);
        return Ok(mapper.Map<BpmnMetaDefinitionDto>(definition));
    }
    
    
    [HttpPut("meta")]
    public async Task<ActionResult<BpmnMetaDefinitionDto>> MetaPut([FromBody] BpmnMetaDefinitionDto dto)
    {
        var definition = mapper.Map<BpmnMetaDefinition>(dto);
        await storageSystem.DefinitionStorage.UpdateMetaDefinition(definition);
        return Ok(mapper.Map<BpmnMetaDefinitionDto>(definition));
    }

 
    #endregion
    
}