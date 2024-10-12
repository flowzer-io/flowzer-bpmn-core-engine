using System.Dynamic;
using AutoMapper;
using BPMN.Common;
using Flowzer.Shared;
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
        CreateMap<ExtendedBpmnMetaDefinitionDto, ExtendedBpmnMetaDefinition>();
        CreateMap<ExtendedBpmnMetaDefinition, ExtendedBpmnMetaDefinitionDto>();
        
        CreateMap<BpmnMetaDefinitionDto, BpmnMetaDefinition>();
        CreateMap<BpmnMetaDefinition,BpmnMetaDefinitionDto>();
                
        CreateMap<BpmnDefinition, BpmnDefinitionDto>();
        CreateMap<BpmnDefinitionDto,BpmnDefinition>();
        
        CreateMap<Version, VersionDto>();
        CreateMap<VersionDto, Version>();    
        
        
        CreateMap<Token, TokenDto>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
            .ForMember(dest => dest.State, opt => opt.MapFrom(src => (FlowNodeStateDto)src.State))
            .ForMember(dest => dest.CurrentFlowNodeId, opt => opt.MapFrom(src => src.CurrentFlowNode != null ? src.CurrentFlowNode.Id : string.Empty))
            .ForMember(dest => dest.CurrentFlowElement, opt => opt.MapFrom(src => src.CurrentFlowNode != null ? src.CurrentFlowNode.ToExpando() : null))
            .ForMember(dest => dest.OutputData, opt => opt.MapFrom(src => src.OutputData != null ? src.OutputData.ToExpando() : null))
            .ForMember(dest => dest.Variables, opt => opt.MapFrom(src => src.Variables != null ? src.Variables.ToExpando() : null))
            .ForMember(dest => dest.PreviousTokenId, opt => opt.MapFrom(src => src.PreviousToken != null ? src.PreviousToken.Id : (Guid?)null))
            .ForMember(dest => dest.ParentTokenId, opt => opt.MapFrom(src => src.ParentTokenId));
        
        CreateMap<UserTaskSubscriptionDto, UserTaskSubscription>();
        CreateMap<UserTaskSubscription, UserTaskSubscriptionDto>();
        
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