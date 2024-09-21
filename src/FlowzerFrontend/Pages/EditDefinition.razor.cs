using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class EditDefinition
{
    [Parameter]
    public string? DefinitionId { get; set; }
    
    [Inject]
    public required IJSRuntime JsRuntime { get; set; }

    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }

    [Inject]
    private IDialogService DialogService { get; set; }
    
    [Inject]
    public required NavigationManager NavigationManager { get; set; }

    public string? ErrorString { get; set; }
    public bool IsDocumentLoading { get; set; } = true;
    
    
    private bool TitleEditMode { get; set; }
    private BpmnMetaDefinitionDto CurrentMetaDefinition { get; set; } = new BpmnMetaDefinitionDto()
    {
        Active = false,
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

        if (string.IsNullOrEmpty(DefinitionId))
        {
            return;
        }
        
        var metaModel = await FlowzerApi.GetMetaDefinitionById(DefinitionId);
        CurrentMetaDefinition = metaModel;

        CurrentDefinition = await FlowzerApi.GetLatestDefinition(DefinitionId);
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
    }

    public BpmnDefinitionDto CurrentDefinition { get; set; } = new BpmnDefinitionDto()
    {
        DefinitionId = "Loading...",
        Hash = "Loading...",
        Id = Guid.Empty,
        PreviousGuid = Guid.Empty,
        SavedByUser = Guid.Empty,
        SavedOn = DateTime.Now,
        Version = new BpmnVersionDto()
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
    

    private async void ToggleTitleEditMode()
    {
        try
        {
            if (TitleEditMode)
                await FlowzerApi.UpdateMetaDefinition(CurrentMetaDefinition);
            
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        TitleEditMode = !TitleEditMode;
        StateHasChanged();
    }


    private void OnTitleEditKeyUp(KeyboardEventArgs keyboardEventArgs)
    {
        if (keyboardEventArgs.Key == "Enter")
        {
            ToggleTitleEditMode();
        }
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
        var saveXmlResult = await JsRuntime.InvokeAsync<JsonObject>("saveXML");
        var xmlData = saveXmlResult["xml"]!.GetValue<string>();
        return xmlData;
    }
}