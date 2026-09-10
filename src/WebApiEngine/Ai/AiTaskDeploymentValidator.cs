using BPMN.Activities;
using BPMN.Common;
using BPMN.Flowzer;
using BPMN.Infrastructure;
using core_engine;
using core_engine.Exceptions;
using Model;
using StorageSystem;

namespace WebApiEngine.Ai;

/// <summary>
/// Bindet die im BPMN gespeicherten Verbindungs- und Werkzeugreferenzen an eine verwendbare
/// Installation. Secret-Werte werden erst unmittelbar vor einem Provideraufruf aufgeloest.
/// </summary>
public static class AiTaskDeploymentValidator
{
    public static System.Threading.Tasks.Task ValidateAsync(
        Definitions model,
        IAiConnectionStorage connectionStorage,
        IAiSecretStore? secretStore,
        AiToolRegistry? toolRegistry = null,
        CancellationToken cancellationToken = default) =>
        ValidateAsync(
            model.GetProcesses().SelectMany(process => AllFlowElements(process.FlowElements)).OfType<ServiceTask>(),
            connectionStorage,
            secretStore,
            toolRegistry,
            cancellationToken);

    public static async System.Threading.Tasks.Task ValidateAsync(
        IEnumerable<ServiceTask> serviceTasks,
        IAiConnectionStorage connectionStorage,
        IAiSecretStore? secretStore,
        AiToolRegistry? toolRegistry = null,
        CancellationToken cancellationToken = default)
    {
        _ = await BindAsync(serviceTasks, connectionStorage, secretStore, toolRegistry, cancellationToken);
    }

    /// <summary>
    /// Prueft alle KI-Verbindungen und liefert zugleich den unveraenderlichen, nicht geheimen
    /// Snapshot fuer die deployte Workflow-Version. Auch Werkzeugvertrag und Freigabemodus
    /// werden exakt gebunden und koennen spaeter nicht aus dem Prompt erweitert werden.
    /// </summary>
    public static async Task<Dictionary<string, BoundAiTask>> BindAsync(
        IEnumerable<ServiceTask> serviceTasks,
        IAiConnectionStorage connectionStorage,
        IAiSecretStore? secretStore,
        AiToolRegistry? toolRegistry = null,
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
                    "extensionElements.aiTask.connectionId",
                    "The referenced AI connection does not exist.");
            if (!connection.Enabled)
                throw Failure("bpmn.ai_task.connection_disabled", task.Id,
                    "extensionElements.aiTask.connectionId",
                    "The referenced AI connection is disabled.");
            if (secretStore is null
                || !await secretStore.ExistsAsync(connection.SecretReference, cancellationToken))
                throw Failure("bpmn.ai_task.connection_not_ready", task.Id,
                    "extensionElements.aiTask.connectionId",
                    "The referenced AI connection is not ready for use.");

            var effectiveModel = string.IsNullOrWhiteSpace(definition.Model)
                ? connection.DefaultModel.Trim()
                : definition.Model.Trim();
            var tools = BindTools(task, connection, toolRegistry);
            bindings.Add(task.Id, new BoundAiTask(
                connection.Id,
                connection.Revision,
                effectiveModel,
                tools.Count == 0 ? null : tools.ToArray()));
        }

        return bindings;
    }

    /// <summary>
    /// Prueft einen gespeicherten Snapshot gegen BPMN und installierte Werkzeugvertraege,
    /// ohne Verbindung oder Modell erneut auf den jeweils neuesten Stand aufzuloesen.
    /// </summary>
    public static void ValidateBindings(
        IEnumerable<ServiceTask> serviceTasks,
        IReadOnlyDictionary<string, BoundAiTask> bindings,
        AiToolRegistry? toolRegistry = null)
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
                && !string.Equals(binding.Model, contract.Model.Trim(), StringComparison.Ordinal)
                || !BindingsMatch(contract.Tools, binding.Tools, toolRegistry))
                throw new InvalidDataException("Stored AI task bindings do not match the workflow definition.");
        }
    }

    private static IReadOnlyList<BoundAiTool> BindTools(
        ServiceTask task,
        AiConnection connection,
        AiToolRegistry? registry)
    {
        var references = task.FlowzerAiTask!.Tools;
        if (references.Count == 0) return [];
        var permissions = AiToolPermissionRules.Effective(connection)
            .ToDictionary(permission => (permission.ToolId, permission.ToolVersion));
        var result = new List<BoundAiTool>(references.Count);
        foreach (var reference in references)
        {
            var path = $"extensionElements.aiTask.tool[{result.Count}]";
            var registered = registry?.Find(reference.ToolId, reference.ToolVersion)
                ?? throw Failure("bpmn.ai_task.tool_not_registered", task.Id, path,
                    "The referenced AI tool version is not registered in this installation.");
            if (!permissions.TryGetValue((reference.ToolId, reference.ToolVersion), out var permission))
                throw Failure("bpmn.ai_task.tool_not_allowed", task.Id, path,
                    "The AI connection does not allow the referenced tool version.");
            if (reference.ApprovalMode == AiToolApprovalMode.Automatic
                && registered.Definition.SideEffect != AiToolSideEffect.ReadOnly)
                throw Failure("bpmn.ai_task.tool_approval_required", task.Id, $"{path}.approval",
                    "A tool with external side effects cannot run automatically.");
            if (reference.ApprovalMode == AiToolApprovalMode.PreApproved
                && (!permission.AllowPreApproval || !registered.Definition.AllowsPreApproval))
                throw Failure("bpmn.ai_task.tool_preapproval_not_allowed", task.Id, $"{path}.approval",
                    "The AI tool is not eligible for workflow pre-approval.");

            result.Add(new BoundAiTool(
                reference.ToolId,
                reference.ToolVersion,
                registered.ContractHash,
                registered.Definition.SideEffect,
                reference.ApprovalMode));
        }
        return result;
    }

    private static bool BindingsMatch(
        IReadOnlyList<AiTaskToolReference> references,
        IReadOnlyList<BoundAiTool>? bindings,
        AiToolRegistry? registry)
    {
        var values = bindings ?? [];
        if (references.Count != values.Count) return false;
        for (var index = 0; index < references.Count; index++)
        {
            var reference = references[index];
            var binding = values[index];
            if (binding.ToolId != reference.ToolId
                || binding.ToolVersion != reference.ToolVersion
                || binding.ApprovalMode != reference.ApprovalMode)
                return false;
            var registered = registry?.Find(binding.ToolId, binding.ToolVersion);
            if (registered is null
                || registered.ContractHash != binding.ContractHash
                || registered.Definition.SideEffect != binding.SideEffect)
                return false;
        }
        return true;
    }

    private static BpmnCapabilityValidationException Failure(
        string code,
        string elementId,
        string propertyPath,
        string message) =>
        new(code, elementId, propertyPath, BpmnCapabilityMatrix.Contract.ContractVersion, message);

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
