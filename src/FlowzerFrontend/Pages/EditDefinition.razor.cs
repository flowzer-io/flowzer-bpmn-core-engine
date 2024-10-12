using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class EditDefinition
{
    [Parameter]
    public string? MetaDefinitionId { get; set; }
        
    [Parameter]
    public Guid? DefinitionId { get; set; }

    
    [Inject]
    public required IJSRuntime JsRuntime { get; set; }

    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }
    
    private FluentMenuButton _menubutton = new();
 
    public string? ErrorString { get; set; }
    public bool IsDocumentLoading { get; set; } = true;
    
    

    private BpmnMetaDefinitionDto CurrentMetaDefinition { get; set; } = new BpmnMetaDefinitionDto()
    {
        Description = "",
        Name = "Loading...",
        DefinitionId = "Loading..."
    };
    
    protected override async Task OnInitializedAsync()
    {
        await JsRuntime.EvalCodeBehindJsScripts(this);
        InitEditor();
    }


    private async void InitEditor()
    {
        await JsRuntime.InvokeVoidAsync("InitEdit");
        await LoadModel();
        
    }
    

    private async Task LoadModel()
    {

        if (string.IsNullOrEmpty(MetaDefinitionId))
        {
            return;
        }
        
        var metaModel = await FlowzerApi.GetMetaDefinitionById(MetaDefinitionId);
        CurrentMetaDefinition = metaModel;

        if (DefinitionId.HasValue)
        {
            CurrentDefinition = await FlowzerApi.GetDefinition(DefinitionId!);
        }
        else //if not definition id is given, load the latest definition
        {
            CurrentDefinition = await FlowzerApi.GetLatestDefinition(MetaDefinitionId);
        }
      
        var xml = await FlowzerApi.GetXmlDefinition(CurrentDefinition.Id);

        if (string.IsNullOrEmpty(xml))
        {
            ErrorString = "No XML found for this model.";
        }
        else
        {
                await LoadDiagramXml(xml);
        }
        
        IsDocumentLoading = false;
        await InvokeAsync(StateHasChanged);
    }

    private BpmnDefinitionDto CurrentDefinition { get; set; } = new BpmnDefinitionDto()
    {
        DefinitionId = "Loading...",
        Hash = "Loading...",
        Id = Guid.Empty,
        PreviousGuid = Guid.Empty,
        SavedByUser = Guid.Empty,
        SavedOn = DateTime.Now,
        Version = new VersionDto()
        {
            Major = 0,
            Minor = 0
        }
    };

    public List<BpmnMetaDefinitionDto> AvailableVersions { get; set; } = new();

    private async Task LoadDiagramXml(string xml)
    {
        await JsRuntime.InvokeVoidAsyncNoneCached("window.bpmnModeler.importXML", xml);
        ErrorString = null;
    }

    private async void OnSaveClick()
    {
        var xmlData = await GetXmlData();
        var definition = await FlowzerApi.UploadDefinition(xmlData);
        CurrentDefinition = definition;
        StateHasChanged();
    }

    private async void OnDeployClick()
    {
        var xmlData = await GetXmlData();
        try
        {
            var definition = await FlowzerApi.DeployDefinition(xmlData);
            CurrentDefinition = definition;
        }
        catch (Exception e)
        {
            var dialog = await DialogService.ShowErrorAsync(e.Message, "Error");
            await dialog.Result;
        }
 
        StateHasChanged();
    }

    private async Task<string> GetXmlData()
    {
        var saveXmlResult = await JsRuntime.InvokeAsyncNoneCached<JsonObject>("window.bpmnModeler.saveXML");
        var xmlData = saveXmlResult["xml"]!.GetValue<string>();
        return xmlData;
    }

    private async void AfterChanged(string obj)
    {
        await FlowzerApi.UpdateMetaDefinition(CurrentMetaDefinition);
    }

    private void OnSaveMenuChoosen(MenuChangeEventArgs obj)
    {
        JsRuntime.InvokeVoidAsync("console.writeline", obj?.Id ?? "null");
    }
}