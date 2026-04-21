using System.Dynamic;
using FlowzerFrontend.BusinessLogic;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Newtonsoft.Json;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Tasks : FlowzerComponentBase
{
    [Inject] public required FlowzerApi FlowzerApi { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }

    private ExtendedUserTaskSubscriptionDto[] _tasks = [];
    private ExtendedBpmnMetaDefinitionDto[] _deployedWorkflows = [];
    private readonly HashSet<string> _busyWorkflows = [];
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    private int OpenTaskCount => _tasks.Length;
    private int StartableWorkflowCount => _deployedWorkflows.Length;

    protected override async Task OnInitializedAsync() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        IsLoading = true;
        LoadErrorMessage = null;
        StateHasChanged();

        try
        {
            var tasksTask = FlowzerApi.GetAllUserTasks();
            var workflowsTask = FlowzerApi.GetAllBpmnMetaDefinitions();
            await Task.WhenAll(tasksTask, workflowsTask);

            _tasks = tasksTask.Result;
            _deployedWorkflows = workflowsTask.Result
                .Where(w => w.DeployedId.HasValue)
                .OrderBy(w => w.Name)
                .ToArray();
        }
        catch (Exception ex)
        {
            LoadErrorMessage = $"Could not load data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OnTaskClick(ExtendedUserTaskSubscriptionDto task)
    {
        try
        {
            var formName = TokenDisplayHelper.GetImplementation(task.Token);
            if (string.IsNullOrWhiteSpace(formName))
            {
                ErrorDialog($"Task \"{task.Name}\" has no form configured. Set the 'Form key' in the BPMN task properties.");
                return;
            }

            string? version = null;
            if (formName.Contains(':'))
            {
                var parts = formName.Split(':');
                formName = parts[0];
                version = parts[1];
            }

            List<FormMetaDataDto> formMetas;
            try { formMetas = await FlowzerApi.GetFormMetaByName(formName); }
            catch (Exception ex) { ErrorDialog($"Could not look up form \"{formName}\": {ex.Message}"); return; }

            var formMeta = formMetas.SingleOrDefault();
            if (formMeta == null)
            {
                ErrorDialog($"No form named \"{formName}\" found. Create the form first or fix the task configuration.");
                return;
            }

            FormDto form;
            try { form = version == null ? await FlowzerApi.GetLatestForm(formMeta.FormId) : await FlowzerApi.GetForm(formMeta.FormId, VersionDto.FromString(version)); }
            catch (Exception ex) { ErrorDialog($"Could not load form \"{formName}\": {ex.Message}"); return; }

            if (string.IsNullOrEmpty(form.FormData))
            {
                ErrorDialog($"Form \"{formName}\" exists but has no content. Open the form editor and save it first.");
                return;
            }

            var parameters = new DialogParameters
            {
                Title = task.Name,
                Width = "min(860px, 92vw)",
                Height = "auto",
                TrapFocus = true,
                Modal = true,
                PreventScroll = true,
                ShowTitle = true,
                SecondaryActionEnabled = true,
                SecondaryAction = "Cancel",
                PrimaryAction = "Submit"
            };

            var data = new FilloutFormParameter
            {
                Schema = form.FormData,
                Data = JsonConvert.SerializeObject(task.Token.Variables)
            };

            var dialog = await DialogService.ShowDialogAsync<FilloutForm>(data, parameters);
            var result = await dialog.Result;

            if (!result.Cancelled && result.Data is FilloutFormParameter formResult && !string.IsNullOrWhiteSpace(formResult.OutData))
            {
                var dataObj = JsonConvert.DeserializeObject<ExpandoObject>(formResult.OutData) ?? new ExpandoObject();
                var flowNodeId = TokenDisplayHelper.GetFlowElementId(task.Token);
                if (string.IsNullOrWhiteSpace(flowNodeId))
                {
                    ErrorDialog("FlowNode Id is missing in token data.");
                    return;
                }

                await FlowzerApi.CompleteUserTask(new UserTaskResultDto
                {
                    FlowNodeId = flowNodeId,
                    TokenId = task.Token.Id,
                    ProcessInstanceId = task.ProcessInstanceId,
                    Data = dataObj
                });

                _tasks = _tasks.Where(t => t.Token.Id != task.Token.Id).ToArray();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            ErrorDialog($"An unexpected error occurred: {ex.Message}");
        }
    }

    private async Task OnStartWorkflowAsync(ExtendedBpmnMetaDefinitionDto workflow)
    {
        _busyWorkflows.Add(workflow.DefinitionId);
        StateHasChanged();

        try
        {
            var instance = await FlowzerApi.StartProcessInstance(workflow.DefinitionId);
            NavigationManager.NavigateTo(UriHelper.GetShowInstanceUrl(instance.InstanceId));
        }
        catch (Exception ex)
        {
            ErrorDialog($"Could not start \"{workflow.Name}\": {ex.Message}");
        }
        finally
        {
            _busyWorkflows.Remove(workflow.DefinitionId);
            StateHasChanged();
        }
    }
}
