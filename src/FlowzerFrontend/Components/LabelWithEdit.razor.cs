using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace FlowzerFrontend.Components;

public partial class LabelWithEdit : ComponentBase
{
    
    [Parameter]
    public string Label { get; set; } = string.Empty;
    
    [Parameter]
    public EventCallback<string> LabelChanged { get; set; }
    
    [Parameter]
    public EventCallback<string> AfterChanged { get; set; }

    
    private bool TitleEditMode { get; set; }
    
    private async Task ToggleTitleEditMode()
    {
        try
        {
            if (TitleEditMode)
            {
                if (LabelChanged.HasDelegate)
                    await LabelChanged.InvokeAsync(Label);
                if (AfterChanged.HasDelegate)
                    await AfterChanged.InvokeAsync(Label);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        TitleEditMode = !TitleEditMode;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnTitleEditKeyUp(KeyboardEventArgs keyboardEventArgs)
    {
        if (keyboardEventArgs.Key == "Enter")
        {
            await ToggleTitleEditMode();
        }
    }
    
}
