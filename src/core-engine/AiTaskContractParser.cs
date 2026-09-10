using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BPMN.Flowzer;
using core_engine.Exceptions;

namespace core_engine;

/// <summary>
/// Eine einzige serverseitige Lesart des exportierbaren KI-Aufgabenvertrags. Parser und
/// Deployment-Validierung verwenden dieselbe Routine, damit ein Feld nie nur im Browser gilt.
/// </summary>
internal static class AiTaskContractParser
{
    internal const string WorkerType = "flowzer.ai.v1";
    internal const string AiTaskContractVersion = "1";
    internal const string Namespace = "https://flowzer.io/schema/bpmn/1.0";

    private static readonly XNamespace FlowzerNamespace = Namespace;
    private static readonly XNamespace BpmnNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace ZeebeNamespace = "http://camunda.org/schema/zeebe/1.0";
    private static readonly Regex ToolIdPattern = new(
        "^[a-z0-9][a-z0-9._-]{0,99}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly HashSet<string> SupportedAttributes = new(StringComparer.Ordinal)
    {
        "contractVersion",
        "connectionId",
        "model",
        "instructionVersion",
        "maxInputTokens",
        "maxOutputTokens",
        "timeoutSeconds"
    };

    internal static bool IsAiTaskCandidate(XElement serviceTask)
    {
        var extensionElements = serviceTask.Elements().FirstOrDefault(element => element.Name.LocalName == "extensionElements");
        return extensionElements?.Elements().Any(element => element.Name.LocalName == "aiTask") == true
               || string.Equals(TaskDefinitionType(extensionElements), WorkerType, StringComparison.Ordinal);
    }

    internal static AiTaskDefinition? Parse(XElement serviceTask)
    {
        var elementId = serviceTask.Attribute("id")?.Value;
        var extensionElements = serviceTask.Elements().FirstOrDefault(element => element.Name.LocalName == "extensionElements");
        var aiTaskCandidates = extensionElements?.Elements()
            .Where(element => element.Name.LocalName == "aiTask")
            .ToArray() ?? [];
        var workerType = TaskDefinitionType(extensionElements);

        if (aiTaskCandidates.Length == 0)
        {
            if (string.Equals(workerType, WorkerType, StringComparison.Ordinal))
                throw Failure("bpmn.ai_task.contract_required", elementId, "extensionElements.aiTask",
                    "The AI service task requires a flowzer:aiTask contract.");
            return null;
        }

        if (aiTaskCandidates.Length > 1)
            throw Failure("bpmn.ai_task.contract_duplicate", elementId, "extensionElements.aiTask",
                "The AI service task must contain exactly one flowzer:aiTask contract.");

        var aiTask = aiTaskCandidates[0];
        if (aiTask.Name.Namespace != FlowzerNamespace)
            throw Failure("bpmn.ai_task.namespace_invalid", elementId, "extensionElements.aiTask",
                "The AI task contract must use the Flowzer BPMN extension namespace.");
        if (!string.Equals(workerType, WorkerType, StringComparison.Ordinal))
            throw Failure("bpmn.ai_task.worker_type_invalid", elementId, "extensionElements.taskDefinition.type",
                $"The AI task requires the reserved worker type '{WorkerType}'.");

        var unsupportedAttribute = aiTask.Attributes()
            .FirstOrDefault(attribute => !attribute.IsNamespaceDeclaration
                && (attribute.Name.Namespace != XNamespace.None || !SupportedAttributes.Contains(attribute.Name.LocalName)));
        if (unsupportedAttribute is not null)
            throw Failure("bpmn.ai_task.attribute_unsupported", elementId,
                $"extensionElements.aiTask.{unsupportedAttribute.Name.LocalName}",
                "The AI task contract contains an unsupported attribute.");

        RequireExactVersion(aiTask, elementId);
        var connectionId = ParseConnectionId(aiTask, elementId);
        var model = OptionalText(aiTask.Attribute("model")?.Value, 200, elementId, "model");
        var instructionVersion = PositiveInteger(aiTask, "instructionVersion", 1, int.MaxValue,
            "bpmn.ai_task.instruction_version_invalid", elementId);
        var maxInputTokens = PositiveInteger(aiTask, "maxInputTokens", 1, 128_000,
            "bpmn.ai_task.limit_invalid", elementId);
        var maxOutputTokens = PositiveInteger(aiTask, "maxOutputTokens", 1, 32_768,
            "bpmn.ai_task.limit_invalid", elementId);
        var timeoutSeconds = PositiveInteger(aiTask, "timeoutSeconds", 1, 300,
            "bpmn.ai_task.limit_invalid", elementId);
        var instruction = RequiredChildText(aiTask, "instruction", 20_000, elementId);
        var resultSchema = RequiredChildText(aiTask, "resultSchema", 50_000, elementId);
        var tools = ParseTools(aiTask, elementId);
        ValidateResultSchema(resultSchema, elementId);
        ValidateMappings(extensionElements!, elementId);

        return new AiTaskDefinition(
            1,
            connectionId,
            model,
            instructionVersion,
            instruction,
            resultSchema,
            maxInputTokens,
            maxOutputTokens,
            timeoutSeconds)
        {
            Tools = tools
        };
    }

    private static string? TaskDefinitionType(XElement? extensionElements) => extensionElements?.Elements()
        .FirstOrDefault(element => element.Name.LocalName == "taskDefinition")
        ?.Attribute("type")?.Value?.Trim();

    private static void RequireExactVersion(XElement aiTask, string? elementId)
    {
        if (!string.Equals(aiTask.Attribute("contractVersion")?.Value, AiTaskContractVersion, StringComparison.Ordinal))
            throw Failure("bpmn.ai_task.contract_version_unsupported", elementId,
                "extensionElements.aiTask.contractVersion",
                $"The AI task contract version must be '{AiTaskContractVersion}'.");
    }

    private static Guid ParseConnectionId(XElement aiTask, string? elementId)
    {
        if (!Guid.TryParse(aiTask.Attribute("connectionId")?.Value, out var connectionId)
            || connectionId == Guid.Empty)
            throw Failure("bpmn.ai_task.connection_invalid", elementId, "extensionElements.aiTask.connectionId",
                "The AI task requires a non-empty stable connection id.");
        return connectionId;
    }

    private static string? OptionalText(
        string? value,
        int maximumLength,
        string? elementId,
        string property)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximumLength)
            throw Failure("bpmn.ai_task.value_too_long", elementId, $"extensionElements.aiTask.{property}",
                "The AI task value exceeds its supported length.");
        return normalized;
    }

    private static int PositiveInteger(
        XElement aiTask,
        string attribute,
        int minimum,
        int maximum,
        string code,
        string? elementId)
    {
        var value = aiTask.Attribute(attribute)?.Value;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum || parsed > maximum)
            throw Failure(code, elementId, $"extensionElements.aiTask.{attribute}",
                "The AI task execution limit is outside the supported range.");
        return parsed;
    }

    private static string RequiredChildText(
        XElement aiTask,
        string childName,
        int maximumLength,
        string? elementId)
    {
        var unsupportedChild = aiTask.Elements()
            .FirstOrDefault(child => child.Name.Namespace != FlowzerNamespace
                || child.Name.LocalName is not ("instruction" or "resultSchema" or "tool"));
        if (unsupportedChild is not null)
            throw Failure("bpmn.ai_task.child_unsupported", elementId,
                $"extensionElements.aiTask.{unsupportedChild.Name.LocalName}",
                "The AI task contract contains an unsupported child element.");

        var children = aiTask.Elements(FlowzerNamespace + childName).ToArray();
        if (children.Length != 1)
            throw Failure($"bpmn.ai_task.{ToSnakeCase(childName)}_required", elementId,
                $"extensionElements.aiTask.{childName}",
                $"The AI task requires exactly one {childName} element.");
        var value = children[0].Value.Trim();
        if (value.Length == 0)
            throw Failure($"bpmn.ai_task.{ToSnakeCase(childName)}_required", elementId,
                $"extensionElements.aiTask.{childName}",
                $"The AI task requires a non-empty {childName}.");
        if (value.Length > maximumLength)
            throw Failure("bpmn.ai_task.value_too_long", elementId,
                $"extensionElements.aiTask.{childName}",
                "The AI task value exceeds its supported length.");
        return value;
    }

    private static IReadOnlyList<AiTaskToolReference> ParseTools(XElement aiTask, string? elementId)
    {
        var elements = aiTask.Elements(FlowzerNamespace + "tool").ToArray();
        if (elements.Length > 20)
            throw Failure("bpmn.ai_task.tool_limit", elementId, "extensionElements.aiTask.tool",
                "The AI task exceeds the supported tool reference limit.");

        var tools = new List<AiTaskToolReference>(elements.Length);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < elements.Length; index++)
        {
            var tool = elements[index];
            var path = $"extensionElements.aiTask.tool[{index}]";
            var unsupportedAttribute = tool.Attributes().FirstOrDefault(attribute =>
                !attribute.IsNamespaceDeclaration
                && (attribute.Name.Namespace != XNamespace.None
                    || attribute.Name.LocalName is not ("id" or "version" or "approval")));
            if (unsupportedAttribute is not null || tool.HasElements || !string.IsNullOrWhiteSpace(tool.Value))
                throw Failure("bpmn.ai_task.tool_contract_unsupported", elementId, path,
                    "The AI tool reference contains unsupported data.");

            var id = tool.Attribute("id")?.Value?.Trim();
            if (id is null || !ToolIdPattern.IsMatch(id))
                throw Failure("bpmn.ai_task.tool_id_invalid", elementId, $"{path}.id",
                    "The AI tool id is invalid.");
            if (!seenIds.Add(id))
                throw Failure("bpmn.ai_task.tool_duplicate", elementId, $"{path}.id",
                    "The AI task must not reference the same tool id more than once.");

            if (!int.TryParse(tool.Attribute("version")?.Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var version)
                || version < 1)
                throw Failure("bpmn.ai_task.tool_version_invalid", elementId, $"{path}.version",
                    "The AI tool version must be a positive integer.");

            var approval = tool.Attribute("approval")?.Value switch
            {
                "automatic" => AiToolApprovalMode.Automatic,
                "human" => AiToolApprovalMode.HumanRequired,
                "preApproved" => AiToolApprovalMode.PreApproved,
                _ => throw Failure("bpmn.ai_task.tool_approval_invalid", elementId, $"{path}.approval",
                    "The AI tool approval mode is invalid.")
            };
            tools.Add(new AiTaskToolReference(id, version, approval));
        }

        return tools;
    }

    private static void ValidateResultSchema(string schema, string? elementId)
    {
        try
        {
            AiResultSchemaProfile.ValidateSchema(schema);
        }
        catch (AiResultSchemaException exception)
        {
            var code = exception.Code switch
            {
                "ai.result_schema.invalid_json" => "bpmn.ai_task.result_schema_invalid",
                "ai.result_schema.object_required" => "bpmn.ai_task.result_schema_object_required",
                _ => "bpmn.ai_task.result_schema_unsupported"
            };
            throw Failure(code, elementId,
                "extensionElements.aiTask.resultSchema",
                "The AI result schema is outside the supported Flowzer profile.");
        }
    }

    private static void ValidateMappings(XElement extensionElements, string? elementId)
    {
        // Validator und Runtime lesen denselben direkten, namensraumgebundenen Vertrag.
        // Ein zweiter Vertrag oder fremde LocalNames dürfen keine zusätzlichen Daten freigeben.
        var mappings = extensionElements.Elements(ZeebeNamespace + "ioMapping").ToArray();
        if (extensionElements.Name != BpmnNamespace + "extensionElements" || mappings.Length != 1)
            throw Failure("bpmn.ai_task.input_mapping_required", elementId,
                "extensionElements.ioMapping", "The AI task must declare exactly one Zeebe ioMapping.");
        var inputs = mappings[0].Elements(ZeebeNamespace + "input").ToArray();
        var outputs = mappings[0].Elements(ZeebeNamespace + "output").ToArray();
        if (inputs.Length == 0 || inputs.Any(element => !IsCompleteMapping(element)))
            throw Failure("bpmn.ai_task.input_mapping_required", elementId,
                "extensionElements.ioMapping.input",
                "The AI task must declare complete input mappings.");
        if (outputs.Length == 0 || outputs.Any(element => !IsCompleteMapping(element)))
            throw Failure("bpmn.ai_task.output_mapping_required", elementId,
                "extensionElements.ioMapping.output",
                "The AI task must declare complete output mappings.");
    }

    private static bool IsCompleteMapping(XElement element) =>
        !string.IsNullOrWhiteSpace(element.Attribute("source")?.Value)
        && !string.IsNullOrWhiteSpace(element.Attribute("target")?.Value);

    private static string ToSnakeCase(string value) => value switch
    {
        "resultSchema" => "result_schema",
        _ => value
    };

    private static BpmnCapabilityValidationException Failure(
        string code,
        string? elementId,
        string? propertyPath,
        string message) => new(code, elementId, propertyPath, BpmnCapabilityMatrix.Contract.ContractVersion, message);
}
