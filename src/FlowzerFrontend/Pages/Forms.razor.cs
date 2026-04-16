using FlowzerFrontend.BusinessLogic;
using Microsoft.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Forms : ComponentBase
{
    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required FlowzerApi FlowzerApi { get; set; }

    private List<FormMetaDataDto> AllForms { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    public string? SearchText { get; set; }

    private IReadOnlyList<FormMetaDataDto> VisibleForms => FormListViewHelper.ApplyQuery(AllForms, SearchText).ToList();
    private int TotalFormCount => AllForms.Count;
    private int VisibleFormCount => VisibleForms.Count;
    private string ResultLabel => SearchText?.Trim() switch
    {
        { Length: > 0 } => $"Showing {VisibleFormCount} of {TotalFormCount} forms",
        _ => $"Showing {VisibleFormCount} forms"
    };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            AllForms = (await FlowzerApi.GetFormMetaDatas()).ToList();
            LoadErrorMessage = null;
        }
        catch (Exception exception)
        {
            AllForms = [];
            LoadErrorMessage = $"Could not load forms. {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnNewClick()
    {
        NavigationManager.NavigateTo(UriHelper.GetNewFormUrl());
    }

    private void OnEditClick(Guid formId)
    {
        NavigationManager.NavigateTo(UriHelper.GetShowFormUrl(formId));
    }
}
