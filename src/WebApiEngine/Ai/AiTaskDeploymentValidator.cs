using BPMN.Activities;
using BPMN.Common;
using BPMN.Infrastructure;
using core_engine;
using core_engine.Exceptions;
using StorageSystem;

namespace WebApiEngine.Ai;

/// <summary>
/// Bindet die im BPMN gespeicherte stabile Verbindungs-ID an eine verwendbare Installation.
/// Dabei wird nur die Existenz des Secrets geprüft: Der geheime Wert wird erst unmittelbar
/// vor einem späteren Provideraufruf aufgelöst und verlässt nie den Secret-Store.
/// </summary>
public static class AiTaskDeploymentValidator
{
    public static System.Threading.Tasks.Task ValidateAsync(
        Definitions model,
        IAiConnectionStorage connectionStorage,
        IAiSecretStore? secretStore,
        CancellationToken cancellationToken = default) =>
        ValidateAsync(
            model.GetProcesses().SelectMany(process => AllFlowElements(process.FlowElements)).OfType<ServiceTask>(),
            connectionStorage,
            secretStore,
            cancellationToken);

    public static async System.Threading.Tasks.Task ValidateAsync(
        IEnumerable<ServiceTask> serviceTasks,
        IAiConnectionStorage connectionStorage,
        IAiSecretStore? secretStore,
        CancellationToken cancellationToken = default)
    {
        foreach (var task in serviceTasks.Where(task => task.FlowzerAiTask is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = task.FlowzerAiTask!;
            var connection = await connectionStorage.Get(definition.ConnectionId);
            if (connection is null)
                throw Failure("bpmn.ai_task.connection_not_found", task.Id,
                    "The referenced AI connection does not exist.");
            if (!connection.Enabled)
                throw Failure("bpmn.ai_task.connection_disabled", task.Id,
                    "The referenced AI connection is disabled.");
            if (secretStore is null
                || !await secretStore.ExistsAsync(connection.SecretReference, cancellationToken))
                throw Failure("bpmn.ai_task.connection_not_ready", task.Id,
                    "The referenced AI connection is not ready for use.");
        }
    }

    private static BpmnCapabilityValidationException Failure(string code, string elementId, string message) =>
        new(code, elementId, "extensionElements.aiTask.connectionId", BpmnCapabilityMatrix.Contract.ContractVersion, message);

    private static IEnumerable<FlowElement> AllFlowElements(IEnumerable<FlowElement> elements)
    {
        foreach (var element in elements)
        {
            yield return element;
            if (element is SubProcess subProcess)
            {
                foreach (var child in AllFlowElements(subProcess.FlowElements)) yield return child;
            }
        }
    }
}
