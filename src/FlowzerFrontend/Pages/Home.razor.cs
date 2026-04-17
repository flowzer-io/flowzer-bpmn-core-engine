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
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    private int WorkflowCount { get; set; }
    private int DeployedWorkflowCount { get; set; }
    private int ActiveInstanceCount { get; set; }
    private int FailedInstanceCount { get; set; }
    private int FormCount { get; set; }
    private int OpenTaskCount => _tasks.Length;

    [Inject] public required FlowzerApi FlowzerApi { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var userTasksTask = FlowzerApi.GetAllUserTasks();
            var workflowsTask = FlowzerApi.GetAllBpmnMetaDefinitions();
            var instancesTask = FlowzerApi.GetAllInstances();
            var formsTask = FlowzerApi.GetFormMetaDatas();

            await Task.WhenAll(userTasksTask, workflowsTask, instancesTask, formsTask);

            _tasks = userTasksTask.Result;

            var workflows = workflowsTask.Result;
            WorkflowCount = workflows.Length;
            DeployedWorkflowCount = workflows.Count(model => model.DeployedId.HasValue);

            var instances = instancesTask.Result;
            ActiveInstanceCount = InstanceListFilterHelper.Apply(instances, InstanceListFilter.Active).Count();
            FailedInstanceCount = InstanceListFilterHelper.Apply(instances, InstanceListFilter.Error).Count();
            FormCount = formsTask.Result.Count;
            LoadErrorMessage = null;
        }
        catch (Exception exception)
        {
            _tasks = [];
            WorkflowCount = 0;
            DeployedWorkflowCount = 0;
            ActiveInstanceCount = 0;
            FailedInstanceCount = 0;
            FormCount = 0;
            LoadErrorMessage = $"Could not load dashboard data. {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OnCardClick(ExtendedUserTaskSubscriptionDto userTaskSubscription)
    {
        var formName = TokenDisplayHelper.GetImplementation(userTaskSubscription.Token);
        if (string.IsNullOrWhiteSpace(formName))
        {
            ErrorDialog("Form Name ('Implementation') is missing in BPMNN file");
            return;
        }

        string? version = null;
        if (formName.Contains(':'))
        {
            var parts = formName.Split(':');
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
            SecondaryActionEnabled = true,
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
            _tasks = _tasks.Where(task => task.Token.Id != userTaskSubscription.Token.Id).ToArray();
            await InvokeAsync(StateHasChanged);
        }
    }
}

public class FlowzerComponentBase : ComponentBase
{
    [Inject] public required IDialogService DialogService { get; set; }

    public void ErrorDialog(string message)
    {
        DialogService.ShowError(message, "Error");
    }
}
