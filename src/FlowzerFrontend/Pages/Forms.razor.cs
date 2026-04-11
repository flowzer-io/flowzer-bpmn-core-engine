using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Forms : ComponentBase
{
    
    [Inject] public required NavigationManager NavigationManager { get; set; }
    
    [Inject] public required  FlowzerApi FlowzerApi { get; set; }
    
    public IQueryable<FormMetaDataDto> Data { get; set; } = new List<FormMetaDataDto>().AsQueryable();
    public IEnumerable<FormMetaDataDto> SelectedItems { get; set; } = new List<FormMetaDataDto>();
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }


    protected override async Task OnInitializedAsync()
    {
        try
        {
            Data = (await FlowzerApi.GetFormMetaDatas()).AsQueryable();
            LoadErrorMessage = null;
        }
        catch (Exception exception)
        {
            Data = Array.Empty<FormMetaDataDto>().AsQueryable();
            LoadErrorMessage = $"Could not load forms. {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnNewClick(MouseEventArgs obj)
    {   
        NavigationManager.NavigateTo(UriHelper.GetNewFormUrl());
    }
}
