using System.Dynamic;
using AutoMapper;
using BPMN.Common;
using Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using WebApiEngine.Shared;
using Version = Model.Version;

namespace WebApiEngine;

// ReSharper disable once UnusedType.Global
public class AutomapperProfile : Profile
{
    public AutomapperProfile()
    {
        CreateMap<BpmnDefinition, BpmnDefinitionDto>();
        CreateMap<BpmnDefinitionDto,BpmnDefinition>();
        
        CreateMap<Version, VersionDto>();
        CreateMap<VersionDto, Version>();
        
        CreateMap<BpmnMetaDefinitionDto, BpmnMetaDefinition>();
        CreateMap<BpmnMetaDefinition, BpmnMetaDefinitionDto>();
        
        CreateMap<MessageSubscription, MessageSubscriptionDto>();
        CreateMap<MessageDefinition, MessageDefinitionDto>();
        
        CreateMap<FormDto, Form>();
        CreateMap<Form, FormDto>();
        
        CreateMap<FormMetaDataDto, FormMetadata>();
        CreateMap<FormMetadata, FormMetaDataDto>();
        
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