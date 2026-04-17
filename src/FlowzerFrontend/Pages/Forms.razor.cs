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
    private bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
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
    private string EmptyStateTitle => HasSearchText
        ? $"No forms match “{SearchText!.Trim()}”"
        : "No forms yet";
    private string EmptyStateDescription => HasSearchText
        ? "Try another search term or clear the current search to get back to the full form catalog."
        : "Create the first reusable form so workflows can collect and validate user input.";

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

    private void ClearSearch()
    {
        SearchText = string.Empty;
    }
}
