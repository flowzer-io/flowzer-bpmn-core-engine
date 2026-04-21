using System.Text.Json;
using FlowzerFrontend.BusinessLogic;
using FlowzerFrontend.Exceptions;
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
    public string? ErrorString { get; set; }
    public bool IsLoading { get; set; } = true;
    public bool IsNew { get; set; }
    private string? PendingFormData { get; set; }
    private bool IsFormDataLoadedIntoIframe { get; set; }
    private string? ActionFeedbackMessage { get; set; }
    private bool ActionFeedbackIsError { get; set; }

    public string CurrentFormVersionLabel => FormData.Version is { Major: > 0 } or { Minor: > 0 }
        ? $"Version {FormData.Version}"
        : IsNew
            ? "Not saved yet"
            : "Version unavailable";

    public FormMetaDataDto CurrentFormMeta { get; set; }
    public FormDto FormData { get; set; } = new()
    {
        FormId = Guid.Empty
    };

    public EditForm()
    {
        CurrentFormMeta = new FormMetaDataDto()
        {
            FormId = Guid.Empty,
            Name = "Loading..."
        };
    }

    protected override async Task OnInitializedAsync()
    {
        var routeState = FormRouteHelper.Parse(FormId);
        if (!string.IsNullOrWhiteSpace(routeState.ErrorMessage))
        {
            ErrorString = routeState.ErrorMessage;
            IsLoading = false;
            return;
        }

        try
        {
            if (routeState.IsCreate)
            {
                IsNew = true;
                CurrentFormMeta = new FormMetaDataDto()
                {
                    FormId = Guid.NewGuid(),
                    Name = "Unnamed Form"
                };
                FormData = new FormDto()
                {
                    Id = Guid.NewGuid(),
                    FormId = CurrentFormMeta.FormId
                };
            }
            else
            {
                IsNew = false;
                var existingFormId = routeState.FormId!.Value;
                CurrentFormMeta = await FlowzerApi.GetFormMetaData(existingFormId);
                FormData = await FlowzerApi.GetLatestForm(existingFormId);
                PendingFormData = FormData.FormData;
            }

            ErrorString = null;
            ClearActionFeedback();
        }
        catch (Exception exception)
        {
            ErrorString = $"Could not load form. {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (IsLoading || IsFormDataLoadedIntoIframe || string.IsNullOrWhiteSpace(PendingFormData) || !string.IsNullOrWhiteSpace(ErrorString))
        {
            return;
        }

        try
        {
            await LoadFormData(PendingFormData);
            IsFormDataLoadedIntoIframe = true;
        }
        catch (Exception exception)
        {
            ErrorString = $"Could not initialize the form editor. {exception.Message}";
            await InvokeAsync(StateHasChanged);
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
                {
                    break;
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "Waiting for iframe to be ready");
            }

            await Task.Delay(100);
        }

        using var doc = JsonDocument.Parse(data);
        await JsRuntime.InvokeAsyncNoneCached<object>("executeInIframe", "iframe", "window.form.setForm", doc.RootElement);
    }

    private async Task SaveFormClicked(MouseEventArgs _)
    {
        try
        {
            ClearActionFeedback();
            await FlowzerApi.SaveFormMetaData(CurrentFormMeta);
            FormData.FormData = await GetFormData();

            FormData = await FlowzerApi.SaveForm(FormData);
            IsNew = false;
            ActionFeedbackIsError = false;
            ActionFeedbackMessage = $"Saved {CurrentFormMeta.Name} as version {FormData.Version}.";
        }
        catch (ApiException e)
        {
            ActionFeedbackIsError = true;
            ActionFeedbackMessage = e.Message;
            ErrorDialog("Server response was:\n" + e.Message);
        }
        catch (Exception e)
        {
            ActionFeedbackIsError = true;
            ActionFeedbackMessage = e.Message;
            ErrorDialog(e.Message + "\n" + e.StackTrace);
        }
    }

    private async Task<string> GetFormData()
    {
        var ret = await JsRuntime.InvokeAsyncNoneCached<object>("executeInIframe", "iframe", "GetFormData");
        return ((JsonElement)ret).GetRawText();
    }

    private async Task AfterTitleChanged(string _)
    {
        if (!IsNew)
        {
            await FlowzerApi.SaveFormMetaData(CurrentFormMeta);
        }
    }

    private async Task TestFormClicked(MouseEventArgs _)
    {
        DialogParameters parameters = new()
        {
            Title = "Form preview",
            Width = "min(860px, 92vw)",
            Height = "auto",
            TrapFocus = true,
            Modal = true,
            PreventScroll = true,
            ShowTitle = true,
            SecondaryActionEnabled = false,
            SecondaryAction = "",
            PrimaryAction = ""
        };

        var testFormParameters = new TestFormParameters()
        {
            Schema = await GetFormData(),
            Data =
                """
                {
                 "firstName": "John",
                 "lastName": "Doe"
                }
                """
        };
        IDialogReference dialog = await DialogService.ShowDialogAsync<Testform>(testFormParameters, parameters);
        await dialog.Result;
        ActionFeedbackIsError = false;
        ActionFeedbackMessage = "Opened the form preview with sample data.";
    }

    private void ClearActionFeedback()
    {
        ActionFeedbackMessage = null;
        ActionFeedbackIsError = false;
    }
}
