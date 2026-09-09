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
        _ = await BindAsync(serviceTasks, connectionStorage, secretStore, cancellationToken);
    }

    /// <summary>
    /// Prueft alle KI-Verbindungen und liefert zugleich den unveraenderlichen, nicht geheimen
    /// Snapshot fuer die deployte Workflow-Version. So kann der Start niemals still die zu
    /// diesem Zeitpunkt gerade neueste Verbindungsrevision oder ein anderes Modell verwenden.
    /// </summary>
    public static async Task<Dictionary<string, BoundAiTask>> BindAsync(
        IEnumerable<ServiceTask> serviceTasks,
        IAiConnectionStorage connectionStorage,
        IAiSecretStore? secretStore,
        CancellationToken cancellationToken = default)
    {
        var bindings = new Dictionary<string, BoundAiTask>(StringComparer.Ordinal);
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

            var effectiveModel = string.IsNullOrWhiteSpace(definition.Model)
                ? connection.DefaultModel.Trim()
                : definition.Model.Trim();
            bindings.Add(task.Id, new BoundAiTask(connection.Id, connection.Revision, effectiveModel));
        }

        return bindings;
    }

    /// <summary>
    /// Prueft einen bereits gespeicherten Deployment-Snapshot nur gegen das unveraenderliche
    /// BPMN. Er wird bewusst nicht erneut gegen die aktuelle Verbindung aufgeloest.
    /// </summary>
    public static void ValidateBindings(
        IEnumerable<ServiceTask> serviceTasks,
        IReadOnlyDictionary<string, BoundAiTask> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var tasks = serviceTasks.Where(task => task.FlowzerAiTask is not null).ToArray();
        if (bindings.Count != tasks.Length)
            throw new InvalidDataException("Stored AI task bindings do not match the workflow definition.");

        foreach (var task in tasks)
        {
            var contract = task.FlowzerAiTask!;
            if (!bindings.TryGetValue(task.Id, out var binding)
                || binding.ConnectionId != contract.ConnectionId
                || binding.ConnectionRevision < 1
                || string.IsNullOrWhiteSpace(binding.Model)
                || binding.Model.Length > 200
                || contract.Model is not null
                && !string.Equals(binding.Model, contract.Model.Trim(), StringComparison.Ordinal))
                throw new InvalidDataException("Stored AI task bindings do not match the workflow definition.");
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
