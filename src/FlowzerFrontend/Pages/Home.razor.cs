using System.Dynamic;
using FlowzerFrontend.BusinessLogic;
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
        var formName = TokenDisplayHelper.GetImplementation(userTaskSubscription.Token);
        if (string.IsNullOrWhiteSpace(formName))
        {
            ErrorDialog("Form Name ('Implementation') is missing in BPMNN file");
            return;
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
            SecondaryActionEnabled = true ,
            SecondaryAction = "Cancel",
            PrimaryAction = "Submit"
        };
        
        var data = new FilloutFormParameter()
        {
            
            Schema = form.FormData,
            Data = JsonConvert.SerializeObject(userTaskSubscription.Token.Variables)
        };
        
        IDialogReference dialog = await DialogService.ShowDialogAsync<FilloutForm>(data, parameters);
        DialogResult? result = await dialog.Result;
        
        if (!result.Cancelled && result.Data is FilloutFormParameter formResult && !string.IsNullOrWhiteSpace(formResult.OutData))
        {
            var dataObj = JsonConvert.DeserializeObject<ExpandoObject>(formResult.OutData) ?? new ExpandoObject();
            var flowNodeId = TokenDisplayHelper.GetFlowElementId(userTaskSubscription.Token);
            if (string.IsNullOrWhiteSpace(flowNodeId))
            {
                ErrorDialog("FlowNode Id is missing in token data.");
                return;
            }

            var userTaskResult = new UserTaskResultDto()
            {
                FlowNodeId = flowNodeId,
                TokenId = userTaskSubscription.Token.Id,
                ProcessInstanceId = userTaskSubscription.ProcessInstanceId,
                Data = dataObj
            };
            await FlowzerApi.CompleteUserTask(userTaskResult);
        }
        
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
