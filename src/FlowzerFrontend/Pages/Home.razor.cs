using Microsoft.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Home : ComponentBase
{
    private UserTaskSubscriptionDto[] _tasks = [];
    [Inject] public required FlowzerApi FlowzerApi { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _tasks = await FlowzerApi.GetUserTasks();
    }
}