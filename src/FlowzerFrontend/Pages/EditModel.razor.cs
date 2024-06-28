using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class EditModel
{
    [Parameter]
    public string? DefinitionId { get; set; }
    
    [Inject]
    public IJSRuntime JsRuntime { get; set; }

    [Inject]
    internal FlowzerApi FlowzerApi { get; set; }

    public bool TitleEditMode { get; set; }
    public string? CurrentModelTitle { get; set; } = "Some test titel.bpmn";
    
    protected override async Task OnInitializedAsync()
    {
        await JsRuntime.EvalCodeBehindJsScripts(this);
        await LoadModel();
    }

    private async Task LoadModel()
    {

        if (string.IsNullOrEmpty(DefinitionId))
        {
            return;
        }
        
        var metaModel = await FlowzerApi.GetMetaDefinitionById(DefinitionId);
        CurrentModelTitle = metaModel.Name;

        var model = await FlowzerApi.GetLatestDefinition(DefinitionId);
        var xml = await FlowzerApi.GetXmlDefinition(model.Id);
        await LoadDiagramXml(xml);
    }

    private async Task LoadDiagramXml(string xml)
    {
        await JsRuntime.InvokeVoidAsync("bpmnModeler.importXML", xml);
    }


    private async Task NewDiagram()
    {
        var data = """

                   <?xml version="1.0" encoding="UTF-8"?>
                   <bpmn2:definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:bpmn2="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" id="Definitions_1" targetNamespace="http://bpmn.io/schema/bpmn" exporter="Camunda Modeler" exporterVersion="4.1.0" xsi:schemaLocation="http://www.omg.org/spec/BPMN/20100524/MODEL BPMN20.xsd">
                     <bpmn2:process id="Process_1" isExecutable="false" />
                     <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                       <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_1" />
                     </bpmndi:BPMNDiagram>
                   </bpmn2:definitions>


                   """;
        
        await WaitForEditorReady();
        await LoadDiagramXml(data);
    }

    private async Task WaitForEditorReady()
    {
        while (await JsRuntime.InvokeAsync<bool>("isReady") != true)
            await Task.Delay(500);
    }


    private void ToggleTitleEditMode()
    {
        TitleEditMode = !TitleEditMode;
    }


    private async Task OnTitleEditKeyUp(KeyboardEventArgs keyboardEventArgs)
    {
        if (keyboardEventArgs.Key == "Enter")
        {
            ToggleTitleEditMode();
        }
    }
    
    
    
}