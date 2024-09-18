using System.Dynamic;
using AutoMapper;
using BPMN.Common;
using Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
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
        
        CreateMap<MessageSubscription, MessageSubscriptionDto>();
        CreateMap<MessageDefinition, MessageDefinitionDto>();
        
        CreateMap<Message, MessageDto>().ForMember(x=>x.Variables, opt => opt.MapFrom(y=> MapVariables(y)));
        CreateMap<MessageDto,Message>().ForMember(x=>x.Variables, opt => opt.MapFrom(y=>
            JsonConvert.SerializeObject(y.Variables)
            ));
        
        
        
    }

    private ExpandoObject? MapVariables(Message y)
    {
        if (y.Variables == null) return null;
        
        return JsonConvert.DeserializeObject<ExpandoObject>(y.Variables, new Newtonsoft.Json.Converters.ExpandoObjectConverter());
    }
}