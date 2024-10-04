using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace FlowzerFrontend.Components;

public partial class LabelWithEdit : ComponentBase
{
    
    [Parameter]
    public string Label { get; set; }
    
    [Parameter]
    public EventCallback<string>? LabelChanged { get; set; }
    
    [Parameter]
    public Action<string>? AfterChanged { get; set; }

    
    private bool TitleEditMode { get; set; }
    
    private async void ToggleTitleEditMode()
    {
        
        try
        {
            if (TitleEditMode)
            {
                if (LabelChanged != null)
                    await LabelChanged.Value.InvokeAsync(Label);
                if (AfterChanged != null)
                    AfterChanged.Invoke(Label);

                
            }

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        TitleEditMode = !TitleEditMode;
        StateHasChanged();
    }

    private void OnTitleEditKeyUp(KeyboardEventArgs keyboardEventArgs)
    {
        if (keyboardEventArgs.Key == "Enter")
        {
            ToggleTitleEditMode();
        }
    }
    
}