using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;


public partial class EditForm : FlowzerComponentBase
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
                Id = Guid.NewGuid(),
                FormId = CurrentFormMeta.FormId,
            };
        }
        else
        {
            IsNew = false;
            CurrentFormMeta = await FlowzerApi.GetFormMetaData(Guid.Parse(FormId));    
            FormData = await FlowzerApi.GetLatestForm(Guid.Parse(FormId));
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
                var ret = await JsRuntime.InvokeAsyncNoneCached<bool>("executeInIframe", "iframe", "IsReady");
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
        await JsRuntime.InvokeAsyncNoneCached<object>("executeInIframe", "iframe", "window.form.setForm",doc.RootElement);
    }


    private async void SaveFormClicked(MouseEventArgs obj)
    {
        try
        {
            await FlowzerApi.SaveFormMetaData(CurrentFormMeta);
            FormData.FormData = await GetFormData();
                        
            FormData = await FlowzerApi.SaveForm(FormData);
            IsNew = false;
        }
        
        catch (ApiException e)
        {
            ErrorDialog("Server response was:\r\n" + e.Message);
        }     
        catch (Exception e)
        {
            ErrorDialog(e.Message + "\r\n" + e.StackTrace);
        }
    }

    private async Task<string?> GetFormData()
    {
        var ret = await JsRuntime.InvokeAsyncNoneCached<object>("executeInIframe", "iframe", "GetFormData");
        return ((JsonElement)ret).GetRawText();
    }
    
    private async void AfterTitleChanged(string obj)
    {
        if (!IsNew) //if the object is new (it was never saved yet) we don't have to inform the server about the change
            await FlowzerApi.SaveFormMetaData(CurrentFormMeta);
    }

    private async void TestFormClicked(MouseEventArgs obj)
    {
        DialogParameters parameters = new()
        {
            Title = $"Test Form",
            Width = "80%",
            Height = "90%",
            TrapFocus = true,
            Modal = true,
            PreventScroll = true,
            ShowTitle = false,
            SecondaryActionEnabled = false,
            SecondaryAction = "",
            PrimaryAction = ""
        };

        var testFormParameters = new TestFormParameters()
        {
            Schema = await GetFormData(),
            Data = """
                   {
                    "firstName": "John",
                    "lastName": "Doe"
                   }
                   """
        };
        IDialogReference dialog = await DialogService.ShowDialogAsync<Testform>(testFormParameters, parameters);
        DialogResult? result = await dialog.Result;

    }
}