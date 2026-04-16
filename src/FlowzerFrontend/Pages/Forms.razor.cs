using FlowzerFrontend.BusinessLogic;
using Microsoft.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Forms : ComponentBase
{
    private string? _searchText;

    [Inject] public required NavigationManager NavigationManager { get; set; }
    [Inject] public required FlowzerApi FlowzerApi { get; set; }

    private List<FormMetaDataDto> AllForms { get; set; } = [];
    private List<FormMetaDataDto> VisibleForms { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value;
            RefreshVisibleForms();
        }
    }

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
            RefreshVisibleForms();
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

    private void RefreshVisibleForms()
    {
        VisibleForms = FormListViewHelper.ApplyQuery(AllForms, SearchText).ToList();
    }
}
