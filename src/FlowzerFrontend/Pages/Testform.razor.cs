using Microsoft.AspNetCore.Components;

namespace FlowzerFrontend.Pages;

public partial class Testform
{
    [Parameter] public TestFormParameters Content { get; set; } = new();
    public string OutData { get; set; } = string.Empty;
}
