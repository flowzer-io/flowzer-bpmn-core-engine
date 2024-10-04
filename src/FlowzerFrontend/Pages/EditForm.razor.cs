using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class EditForm : ComponentBase
{
    [Inject] public required FlowzerApi FlowzerApi { get; set; }
    [Inject] public required IJSRuntime JsRuntime { get; set; }
    [Inject] public required ILogger<EditForm> Logger { get; set; }

    
    [Parameter] public string FormId { get; set; } = string.Empty;

    public bool IsNew { get; set; }

    public FormMetaDataDto CurrentFormMeta { get; set; } 
    public FormDto FormData { get; set; }


    public EditForm()
    {
        CurrentFormMeta = new FormMetaDataDto()
        {
            FormId = Guid.Empty,
            Name = "Loading...",
        };
    }

    protected override async Task OnInitializedAsync()
    {
        if (string.Compare(FormId, "create", StringComparison.OrdinalIgnoreCase) == 0)
        {
            IsNew = true;
            CurrentFormMeta = new FormMetaDataDto()
            {
                FormId = Guid.NewGuid(),
                Name = "Unnamed Form",
            };
            FormData = new FormDto()
            {
                FormId = CurrentFormMeta.FormId,
            };
        }
        else
        {
            IsNew = false;
            CurrentFormMeta = await FlowzerApi.GetFormMetaData(Guid.Parse(FormId));    
            FormData = await FlowzerApi.GetForm(Guid.Parse(FormId));
            if (!string.IsNullOrEmpty(FormData.FormData))
                await LoadFormData(FormData.FormData);
        }
    }

    private async Task LoadFormData(string data)
    {
        while (true)
        {
            try
            {
                var ret = await JsRuntime.InvokeAsyncNoneCached<bool>("executeInIframe", "IsReady");
                Logger.LogInformation("IsReady: {IsReady}", ret);
                if (ret)
                    break;
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "Waiting for iframe to be ready");
            }

            await Task.Delay(100);
        }
        
        using var doc = JsonDocument.Parse(data);
        await JsRuntime.InvokeAsyncNoneCached<object>("executeInIframe", "window.form.setForm",doc.RootElement);
    }


    private async void SaveForm(MouseEventArgs obj)
    {
        try
        {
            
            await FlowzerApi.SaveFormMetaData(CurrentFormMeta);
            FormData.FormData = await GetFormData();
            await FlowzerApi.SaveForm(FormData);
        }
        catch (Exception e)
        {
            //TODO: Was zutun?
            Console.WriteLine(e);
        }
    }

    private async Task<string?> GetFormData()
    {
        var ret = await JsRuntime.InvokeAsyncNoneCached<object>("executeInIframe", "GetFormData");
        return ((JsonElement)ret).GetRawText();
    }
    
    private async void AfterTitleChanged(string obj)
    {
        await FlowzerApi.SaveFormMetaData(CurrentFormMeta);
    }
    
}