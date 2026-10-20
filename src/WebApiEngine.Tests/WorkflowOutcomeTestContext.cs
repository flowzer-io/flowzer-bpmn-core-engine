using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Reviewte synthetische Bindung vor dem ersten Start; weder echte Konfiguration noch externe Ablage.</summary>
internal sealed class WorkflowOutcomeTestContext : IDisposable
{
    internal TickyTaskVacationTestContext Demo { get; }

    private WorkflowOutcomeTestContext(Action<IServiceCollection>? configureServices = null) =>
        Demo = new TickyTaskVacationTestContext(configureServices: configureServices);
    internal WorkflowOutcomeOptions Options => Demo.Services.GetRequiredService<IOptions<WorkflowOutcomeOptions>>().Value;
    internal WorkflowOutcomeDefinitionOptions Mapping => Options.Definitions.Single();
    internal WorkflowOutcomeProjector Projector => Demo.Services.GetRequiredService<WorkflowOutcomeProjector>();

    internal static async Task<WorkflowOutcomeTestContext> CreateAsync(bool approve = false,
        Action<IServiceCollection>? configureServices = null)
    {
        var context = new WorkflowOutcomeTestContext(configureServices);
        try
        {
            await context.Demo.InitializeAsync();
            // Nur diese lokale Fixture füllt den serverseitigen Optionswert. IDs/Inhaltshash
            // sind bereits fest, bevor der erste HTTP-Start und die erste GET-Projektion laufen.
            context.Options.Definitions.Add(new WorkflowOutcomeDefinitionOptions
            {
                DefinitionId = context.Demo.DefinitionId.ToString("D"),
                CatalogId = TickyTaskVacationTestContext.DefinitionKey,
                ProcessId = "Process_TickyTaskVacation", ApprovedRootEndId = "End_Approved", RejectedRootEndId = "End_Rejected",
                BpmnSha256 = Hash(await context.Demo.Storage.DefinitionStorage.GetBinary(context.Demo.DefinitionId))
            });
            await context.Demo.StartAsync();
            if (approve)
            {
                await context.ApproveAsync();
                // Jeder Negativtest beginnt mit einer wirklich positiven Projektion:
                // Ein pauschal Unknown liefernder Stub darf Sicherheitsfälle nicht scheinbar grün machen.
                (await context.Projector.ProjectAsync(await context.Demo.InstanceAsync()))
                    .Should().Be(ProcessInstanceOutcomeDto.Approved);
            }
            return context;
        }
        catch { context.Dispose(); throw; }
    }

    internal async Task ApproveAsync()
    {
        foreach (var node in new[] { "Task_Supervisor", "Task_Personnel", "Task_Substitute" })
            await Demo.CompleteAsync(node);
    }

    internal static string Hash(string xml) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
    public void Dispose() => Demo.Dispose();
}
