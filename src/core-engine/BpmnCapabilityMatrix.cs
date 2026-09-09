using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using core_engine.Exceptions;

namespace core_engine;

/// <summary>
/// Liest und erzwingt den einen versionierten BPMN-Fähigkeitsvertrag. Die Validierung arbeitet
/// auf XML-Ebene, weil der Parser nicht ausführbare Altpfade bewusst weiterhin lesen darf, ein
/// neuer Upload sie aber nicht als ausführbaren Workflow speichern soll.
/// </summary>
public static class BpmnCapabilityMatrix
{
    private const string CapabilityResourceSuffix = "Contracts.bpmn_capabilities.v3.json";
    private static readonly Lazy<BpmnCapabilityContract> ContractLoader = new(LoadContract);

    /// <summary>Der unveränderte Vertrag, den Hosts zur Information ihrer Modellieransichten ausliefern können.</summary>
    public static BpmnCapabilityContract Contract => ContractLoader.Value;

    /// <summary>
    /// Prüft, ob alle ausführbaren Prozessbestandteile vom aktuellen Vertrag getragen werden.
    /// Der erste Fehler in Dokumentreihenfolge wird absichtlich gemeldet: So bleibt der API-
    /// Vertrag klein und jede Modellieransicht kann nach einer Korrektur deterministisch erneut prüfen.
    /// </summary>
    public static void ValidateForDeployment(string xml)
        => Validate(xml);

    /// <summary>
    /// Prüft einen Autorenentwurf gegen denselben ausführbaren Elementvertrag. Zusätzliche
    /// fachliche Pflichtwerte werden danach von den spezialisierten Vertragsprüfern validiert.
    /// </summary>
    public static void ValidateForAuthoring(string xml)
        => Validate(xml);

    private static void Validate(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException)
        {
            // Parserdetails können Fragmente des untrusted Uploads enthalten. Der öffentliche
            // Vertrag bleibt deshalb bewusst stabil und wertefrei.
            throw Failure("bpmn.xml.invalid", null, null, "The BPMN XML document is invalid.");
        }
        var executableProcesses = document.Root?
            .Elements()
            .Where(element => element.Name.LocalName == "process"
                && string.Equals(element.Attribute("isExecutable")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];

        if (executableProcesses.Length == 0)
        {
            throw Failure("bpmn.process.executable_required", document.Root?.Attribute("id")?.Value, null,
                "The BPMN definition must contain at least one executable process.");
        }

        foreach (var process in executableProcesses)
        {
            ValidateContainer(process);
        }
    }

    private static void ValidateContainer(XElement container)
    {
        var flowElements = container.Elements().Where(IsFlowElement).ToArray();
        var knownIds = flowElements
            .Select(element => element.Attribute("id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in flowElements)
        {
            ValidateUniqueElementId(element, uniqueIds);
            ValidateElement(element, knownIds);
        }

        ValidateFlowGraph(flowElements);

        foreach (var element in flowElements)
        {
            if (element.Name.LocalName == "subProcess")
            {
                ValidateContainer(element);
            }
        }
    }

    private static bool IsFlowElement(XElement element) => element.Name.LocalName is not (
        "extensionElements" or "incoming" or "outgoing" or "documentation" or "laneSet" or "lane");

    private static void ValidateUniqueElementId(XElement element, ISet<string> uniqueIds)
    {
        var elementId = element.Attribute("id")?.Value;
        if (!string.IsNullOrWhiteSpace(elementId) && !uniqueIds.Add(elementId))
        {
            throw Failure("bpmn.element.id_duplicate", elementId, "id",
                $"The BPMN element id '{elementId}' must be unique within its container.");
        }
    }

    private static void ValidateFlowGraph(IReadOnlyCollection<XElement> flowElements)
    {
        var flowNodes = flowElements
            // Boundary-Events werden durch das angeheftete Activity-Ereignis aktiviert und
            // besitzen deshalb absichtlich keinen eingehenden SequenceFlow.
            .Where(element => element.Name.LocalName is not ("sequenceFlow" or "boundaryEvent"))
            .ToArray();
        var activationRoots = flowElements
            .Where(element => element.Name.LocalName is "startEvent" or "boundaryEvent")
            .Select(element => element.Attribute("id")!.Value)
            .ToArray();

        // Container ohne Startereignis werden weiterhin vom bisherigen Fähigkeitsvertrag akzeptiert.
        // Sobald ein Start existiert, muss jeder Knoten im selben Container von einem davon erreichbar sein.
        if (activationRoots.Length > 0)
        {
            var reachableNodeIds = FindReachableNodeIds(flowElements, activationRoots);
            foreach (var node in flowNodes)
            {
                var nodeId = node.Attribute("id")!.Value;
                if (!reachableNodeIds.Contains(nodeId))
                {
                    throw Failure("bpmn.flow_node.unreachable", nodeId, "incoming",
                        $"The flow node '{nodeId}' is not reachable from a start or boundary event in the same container.");
                }
            }
        }

        ValidateExclusiveGatewaySplits(flowElements);
    }

    private static ISet<string> FindReachableNodeIds(IEnumerable<XElement> flowElements, IEnumerable<string> startNodeIds)
    {
        var outgoingTargets = flowElements
            .Where(element => element.Name.LocalName == "sequenceFlow")
            .GroupBy(element => element.Attribute("sourceRef")!.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(element => element.Attribute("targetRef")!.Value).ToArray(),
                StringComparer.Ordinal);
        var reachableNodeIds = new HashSet<string>(startNodeIds, StringComparer.Ordinal);
        var pendingNodeIds = new Queue<string>(reachableNodeIds);

        while (pendingNodeIds.TryDequeue(out var currentNodeId))
        {
            if (!outgoingTargets.TryGetValue(currentNodeId, out var targetNodeIds))
            {
                continue;
            }

            foreach (var targetNodeId in targetNodeIds)
            {
                if (reachableNodeIds.Add(targetNodeId))
                {
                    pendingNodeIds.Enqueue(targetNodeId);
                }
            }
        }

        return reachableNodeIds;
    }

    private static void ValidateExclusiveGatewaySplits(IReadOnlyCollection<XElement> flowElements)
    {
        var sequenceFlowsBySource = flowElements
            .Where(element => element.Name.LocalName == "sequenceFlow")
            .GroupBy(element => element.Attribute("sourceRef")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var gateway in flowElements.Where(element => element.Name.LocalName == "exclusiveGateway"))
        {
            var gatewayId = gateway.Attribute("id")!.Value;
            if (!sequenceFlowsBySource.TryGetValue(gatewayId, out var outgoingFlows) || outgoingFlows.Length < 2)
            {
                continue;
            }

            var defaultFlowId = gateway.Attribute("default")?.Value;
            if (!string.IsNullOrWhiteSpace(defaultFlowId)
                && !outgoingFlows.Any(flow => string.Equals(flow.Attribute("id")?.Value, defaultFlowId, StringComparison.Ordinal)))
            {
                throw Failure("bpmn.exclusive_gateway.default.invalid_reference", gatewayId, "default",
                    $"The default flow '{defaultFlowId}' of exclusive gateway '{gatewayId}' must be outgoing from that gateway.");
            }

            foreach (var flow in outgoingFlows.Where(flow => !string.Equals(flow.Attribute("id")?.Value, defaultFlowId, StringComparison.Ordinal)))
            {
                if (!flow.Elements().Any(element => element.Name.LocalName == "conditionExpression"
                    && !string.IsNullOrWhiteSpace(element.Value)))
                {
                    var flowId = flow.Attribute("id")!.Value;
                    throw Failure("bpmn.exclusive_gateway.condition_required", flowId, "conditionExpression",
                        $"The non-default sequence flow '{flowId}' of exclusive gateway '{gatewayId}' requires a conditionExpression.");
                }
            }
        }
    }

    private static void ValidateElement(XElement element, ISet<string> knownIds)
    {
        var elementId = element.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(elementId))
        {
            throw Failure("bpmn.element.id_required", null, null,
                $"The '{element.Name.LocalName}' element requires a non-empty id.");
        }

        var capabilityType = GetCapabilityType(element, elementId);
        var capability = Contract.Elements.SingleOrDefault(item => item.ElementType == capabilityType);
        if (capability is null)
        {
            var isEvent = element.Name.LocalName is "startEvent" or "intermediateCatchEvent" or "boundaryEvent" or "endEvent";
            throw Failure(isEvent ? "bpmn.event_definition.unsupported" : "bpmn.element.unsupported", elementId,
                isEvent ? "eventDefinition" : null,
                $"The BPMN element '{element.Name.LocalName}' is not supported by capability contract v{Contract.ContractVersion}.");
        }

        if (!capability.Executable)
        {
            throw Failure("bpmn.element.not_executable", elementId, null,
                $"The BPMN element '{element.Name.LocalName}' is parsed but not executable in capability contract v{Contract.ContractVersion}.");
        }

        ValidateRequiredConfiguration(element, elementId, knownIds);
    }

    private static string GetCapabilityType(XElement element, string elementId)
    {
        var elementType = element.Name.LocalName;
        if (elementType == "serviceTask" && AiTaskContractParser.IsAiTaskCandidate(element))
        {
            return "serviceTask.aiTask";
        }
        if (elementType is not ("startEvent" or "intermediateCatchEvent" or "boundaryEvent" or "endEvent"))
        {
            return elementType;
        }

        var eventDefinitions = element.Descendants()
            .Where(descendant => descendant.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal))
            .Select(descendant => descendant.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (eventDefinitions.Length > 1)
        {
            throw Failure("bpmn.event_definition.invalid", elementId, "eventDefinition",
                $"The BPMN event '{elementId}' must not contain more than one event definition.");
        }
        if (eventDefinitions.Length == 0)
        {
            return elementType is "endEvent" or "startEvent" ? $"{elementType}.plain" : elementType;
        }

        return $"{elementType}.{eventDefinitions[0]}";
    }

    private static void ValidateRequiredConfiguration(XElement element, string elementId, ISet<string> knownIds)
    {
        switch (element.Name.LocalName)
        {
            case "sequenceFlow":
                ValidateSequenceFlow(element, elementId, knownIds);
                break;
            case "serviceTask":
                var aiTask = AiTaskContractParser.Parse(element);
                if (aiTask is null && !HasTaskDefinitionType(element))
                    throw Failure("bpmn.service_task.implementation_required", elementId,
                        "extensionElements.taskDefinition.type",
                        $"The service task '{elementId}' requires zeebe:taskDefinition/@type.");
                break;
            case "userTask" when !HasFormKey(element):
                throw Failure("bpmn.user_task.form_required", elementId,
                    "extensionElements.formDefinition.formKey",
                    $"The user task '{elementId}' requires formDefinition/@formKey or @formId.");
            case "startEvent" or "intermediateCatchEvent" or "boundaryEvent" when HasEventDefinition(element, "timerEventDefinition")
                && !HasTimerSchedule(element):
                throw Failure("bpmn.timer.definition_required", elementId, "timerEventDefinition",
                    $"The timer event '{elementId}' requires timeCycle, timeDate, or timeDuration.");
        }
    }

    private static void ValidateSequenceFlow(XElement element, string elementId, ISet<string> knownIds)
    {
        var sourceRef = element.Attribute("sourceRef")?.Value;
        var targetRef = element.Attribute("targetRef")?.Value;
        if (string.IsNullOrWhiteSpace(sourceRef) || string.IsNullOrWhiteSpace(targetRef)
            || !knownIds.Contains(sourceRef) || !knownIds.Contains(targetRef))
        {
            throw Failure("bpmn.sequence_flow.invalid_reference", elementId, "sourceRef/targetRef",
                $"The sequence flow '{elementId}' must reference existing sourceRef and targetRef elements in the same container.");
        }
    }

    private static bool HasTaskDefinitionType(XElement element) => element.Descendants()
        .Any(descendant => descendant.Name.LocalName == "taskDefinition"
            && !string.IsNullOrWhiteSpace(descendant.Attribute("type")?.Value));

    private static bool HasFormKey(XElement element) => element.Descendants()
        .Any(descendant => descendant.Name.LocalName == "formDefinition"
            && (!string.IsNullOrWhiteSpace(descendant.Attribute("formKey")?.Value)
                || !string.IsNullOrWhiteSpace(descendant.Attribute("formId")?.Value)));

    private static bool HasEventDefinition(XElement element, string eventDefinitionName) => element.Descendants()
        .Any(descendant => descendant.Name.LocalName == eventDefinitionName);

    private static bool HasTimerSchedule(XElement element) => element.Descendants()
        .Any(descendant => descendant.Name.LocalName is "timeCycle" or "timeDate" or "timeDuration");

    private static BpmnCapabilityValidationException Failure(
        string code, string? elementId, string? propertyPath, string message) =>
        new(code, elementId, propertyPath, Contract.ContractVersion, message);

    private static BpmnCapabilityContract LoadContract()
    {
        var assembly = typeof(BpmnCapabilityMatrix).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(CapabilityResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The embedded BPMN capability contract could not be found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded BPMN capability contract could not be opened.");
        var contract = JsonSerializer.Deserialize<BpmnCapabilityContract>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (contract is null || string.IsNullOrWhiteSpace(contract.ContractVersion) || contract.Elements.Count == 0)
        {
            throw new InvalidOperationException("The embedded BPMN capability contract is invalid.");
        }

        return contract;
    }
}
