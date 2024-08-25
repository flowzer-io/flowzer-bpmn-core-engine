using System.Security.Cryptography;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Instance
{
    [Parameter]
    public Guid InstanceGuid { get; set; }
    
    [Inject]
    public required IJSRuntime JsRuntime { get; set; }
    
    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }
    
    public string ErrorString { get; set; }
    
    
    protected override async Task OnInitializedAsync()
    {
        await JsRuntime.EvalCodeBehindJsScripts(this);
        await WaitForInitComplete();
        await InitViewer();
        await LoadData();
    }

    private async Task LoadData()
    {
        var instance = await FlowzerApi.GetProcessInstance(InstanceGuid);
        var data = await FlowzerApi.GetXmlDefinition(instance.DefinitionId);
        await LoadDiagramXml(data);
        await ShowTokens(instance.Tokens);
    }

    private async Task ShowTokens(List<TokenDto> instanceTokens)
    {
        foreach (var instanceToken in instanceTokens.Where(x=>IsActive(x.State)) .GroupBy(x=>x.CurrentFlowNodeId))
        {
            await AddToken(instanceToken.Key, instanceToken.Count());
        }
    }

    private bool IsActive(FlowNodeStateDto state)
    {
        return state == FlowNodeStateDto.Active;
    }

    private async Task AddToken(string? instanceTokenKey, int instanceTokensCount)
    {
        if (instanceTokenKey == null)
            return;
        await JsRuntime.InvokeVoidAsync("addToken", instanceTokenKey, instanceTokensCount);
    }

    private async Task InitViewer()
    {
        await JsRuntime.InvokeVoidAsync("InitViewer");
        
    }
    private async Task LoadDiagramXml(string xml)
    {
        await JsRuntime.InvokeVoidAsync("importXML", xml);
        ErrorString = null;
    }

    


    private async Task WaitForInitComplete()
    {
        while (await JsRuntime.InvokeAsync<bool>("isReady") != true)
            await Task.Delay(500);
    }

}