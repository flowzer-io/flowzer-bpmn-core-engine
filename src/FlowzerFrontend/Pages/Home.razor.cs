using System.Dynamic;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Newtonsoft.Json;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Home : FlowzerComponentBase
{
    private ExtendedUserTaskSubscriptionDto[] _tasks = [];
    [Inject] public required FlowzerApi FlowzerApi { get; set; }
 
    
    protected override async Task OnInitializedAsync()
    {
        _tasks = await FlowzerApi.GetAllUserTasks();
    }
    
    private async void OnCardClick(ExtendedUserTaskSubscriptionDto userTaskSubscription)
    {
        var formName = (string)((dynamic)userTaskSubscription.Token.CurrentFlowElement!).Implementation;
        if (string.IsNullOrEmpty(formName))
        {
            ErrorDialog("Form Name ('Implementation') is missing in BPMNN file");
        }

        string? version = null;
        if (formName.Contains(":"))
        {
            var parts = formName.Split(":");
            formName = parts[0];
            version = parts[1];
        }
        
        var formMeta = (await FlowzerApi.GetFormMetaByName(formName)).SingleOrDefault();
        
        if (formMeta == null)
        {
            ErrorDialog($"Could not find any form Named with '{formName}'");
            return;
        }
        
        FormDto form;
        if (version == null)
        {
            form = await FlowzerApi.GetLatestForm(formMeta.FormId);
        }
        else
        {
            form = await FlowzerApi.GetForm(formMeta.FormId, VersionDto.FromString(version));
        }

        if (string.IsNullOrEmpty(form.FormData))
        {
            ErrorDialog("Form Data is missing in Formulardata");
            return;
        }

        
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
        
        var data = new FilloutFormParameter()
        {
            
            Schema = form.FormData,
            Data = JsonConvert.SerializeObject(userTaskSubscription.Token.Variables)
        };
        
        IDialogReference dialog = await DialogService.ShowDialogAsync<FilloutForm>(data, parameters);
        DialogResult? result = await dialog.Result;
    }


}

public class FlowzerComponentBase: ComponentBase
{
    [Inject] public required IDialogService DialogService { get; set; }
    
    public void ErrorDialog(string message)
    {
        DialogService.ShowError(message, "Error");
    }

}