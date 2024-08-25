using AutoMapper;
using Model;
using WebApiEngine.Shared;

namespace WebApiEngine;

// ReSharper disable once UnusedType.Global
public class AutomapperProfile : Profile
{
    public AutomapperProfile()
    {
        CreateMap<BpmnDefinition, BpmnDefinitionDto>();
        CreateMap<BpmnDefinitionDto,BpmnDefinition>();
        
        CreateMap<BpmnVersion, BpmnVersionDto>();
        CreateMap<BpmnVersionDto, BpmnVersion>();
        
        CreateMap<BpmnMetaDefinitionDto, BpmnMetaDefinition>();
        CreateMap<BpmnMetaDefinition, BpmnMetaDefinitionDto>();
        
    }
}