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
    private const string CapabilityResourceSuffix = "Contracts.bpmn_capabilities.v8.json";
    private static readonly Lazy<BpmnCapabilityContract> ContractLoader = new(LoadContract);

    /// <summary>Der unveränderte Vertrag, den Hosts zur Information ihrer Modellieransichten ausliefern können.</summary>
    public static BpmnCapabilityContract Contract => ContractLoader.Value;

    /// <summary>
    /// Prüft, ob alle ausführbaren Prozessbestandteile vom aktuellen Vertrag getragen werden.
    /// Der erste Fehler in Dokumentreihenfolge wird absichtlich gemeldet: So bleibt der API-
    /// Vertrag klein und jede Modellieransicht kann nach einer Korrektur deterministisch erneut prüfen.
    /// </summary>
    public static void ValidateForDeployment(string xml)
        => Validate(xml, allowToolAuthoring: false);

    /// <summary>
    /// Prüft einen Autorenentwurf gegen denselben ausführbaren Elementvertrag. Zusätzliche
    /// fachliche Pflichtwerte werden danach von den spezialisierten Vertragsprüfern validiert.
    /// </summary>
    public static void ValidateForAuthoring(string xml)
        => Validate(xml, allowToolAuthoring: true);

    private static void Validate(string xml, bool allowToolAuthoring)
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

        List<BpmnCapabilityIssue> issues = [];
        foreach (var process in executableProcesses) ValidateContainer(process, allowToolAuthoring, issues);
        if (issues.Count > 0)
        {
            var first = issues[0];
            throw new BpmnCapabilityValidationException(first.Code, first.ElementId, first.PropertyPath,
                Contract.ContractVersion, first.Message) { Issues = issues };
        }
    }

    private static void ValidateContainer(XElement container, bool allowToolAuthoring, List<BpmnCapabilityIssue> issues)
    {
        var flowElements = container.Elements().Where(IsFlowElement).ToArray();
        var knownIds = flowElements
            .Select(element => element.Attribute("id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);

        var before = issues.Count;
        foreach (var element in flowElements)
        {
            try
            {
                ValidateUniqueElementId(element, uniqueIds);
                ValidateElement(element, knownIds, allowToolAuthoring);
            }
            catch (BpmnCapabilityValidationException failure) { issues.AddRange(failure.Issues); }
        }

        // Graphprüfungen setzen valide IDs und Verweise voraus. Bei fehlerhaften
        // Elementen nicht mit Folgefehlern oder Nullreferenzen die eigentlichen Befunde verdecken.
        if (before == issues.Count)
        {
            try { ValidateFlowGraph(flowElements); }
            catch (BpmnCapabilityValidationException failure) { issues.AddRange(failure.Issues); }
        }

        foreach (var element in flowElements)
        {
            if (element.Name.LocalName == "subProcess")
            {
                ValidateContainer(element, allowToolAuthoring, issues);
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

    private static void ValidateElement(XElement element, ISet<string> knownIds, bool allowToolAuthoring)
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
            var isEvent = IsEventWithDefinition(element.Name.LocalName);
            throw Failure(isEvent ? "bpmn.event_definition.unsupported" : "bpmn.element.unsupported", elementId,
                isEvent ? "eventDefinition" : null,
                $"The BPMN element '{element.Name.LocalName}' is not supported by capability contract v{Contract.ContractVersion}.");
        }

        if (!capability.Executable)
        {
            throw Failure("bpmn.element.not_executable", elementId, null,
                $"The BPMN element '{element.Name.LocalName}' is parsed but not executable in capability contract v{Contract.ContractVersion}.");
        }

        ValidateRequiredConfiguration(element, elementId, knownIds, allowToolAuthoring);
    }

    private static string GetCapabilityType(XElement element, string elementId)
    {
        var elementType = element.Name.LocalName;
        if (elementType == "serviceTask" && AiTaskContractParser.IsAiTaskCandidate(element))
        {
            return "serviceTask.aiTask";
        }
        if (!IsEventWithDefinition(elementType))
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
            return elementType is "endEvent" or "startEvent" or "intermediateThrowEvent"
                ? $"{elementType}.plain"
                : elementType;
        }

        return $"{elementType}.{eventDefinitions[0]}";
    }

    /// <summary>
    /// Ereignisarten, deren Fähigkeit von ihrer Ereignisdefinition abhängt. Ein Intermediate-Throw
    /// gehört seit Vertrag 6 dazu: Ohne diese Unterscheidung trüge ein Signalwurf dieselbe
    /// Fähigkeit wie ein Nachrichtenwurf, obwohl nur der zweite ausgeführt wird.
    /// </summary>
    private static bool IsEventWithDefinition(string elementType) => elementType is
        "startEvent" or "intermediateCatchEvent" or "intermediateThrowEvent" or "boundaryEvent" or "endEvent";

    private static void ValidateRequiredConfiguration(
        XElement element,
        string elementId,
        ISet<string> knownIds,
        bool allowToolAuthoring)
    {
        switch (element.Name.LocalName)
        {
            case "sequenceFlow":
                ValidateSequenceFlow(element, elementId, knownIds);
                break;
            case "serviceTask":
                var aiTask = AiTaskContractParser.Parse(element);
                if (aiTask is { Tools.Count: > 0 } && !allowToolAuthoring)
                    throw Failure("bpmn.ai_task.tools_runtime_unavailable", elementId,
                        "extensionElements.aiTask.tool",
                        "AI tools cannot be deployed before durable approvals and execution are available.");
                if (aiTask is null && !HasTaskDefinitionType(element))
                    throw Failure("bpmn.service_task.implementation_required", elementId,
                        "extensionElements.taskDefinition.type",
                        $"The service task '{elementId}' requires zeebe:taskDefinition/@type.");
                break;
            case "callActivity":
                ValidateCallActivity(element, elementId);
                break;
            case "businessRuleTask":
                ValidateBusinessRuleTask(element, elementId);
                break;
            case "userTask" when !HasFormKey(element):
                throw Failure("bpmn.user_task.form_required", elementId,
                    "extensionElements.formDefinition.formKey",
                    $"The user task '{elementId}' requires formDefinition/@formKey or @formId.");
            // Ein sendendes Element braucht ein Ziel: entweder die Nachricht, die es intern
            // korreliert, oder den Auftragstyp des Workers, der sie nach draussen traegt.
            // Ohne beides waere es ein Schritt, der nichts tut und nichts meldet.
            case "sendTask" or "intermediateThrowEvent" or "endEvent"
                when IsMessageThrow(element) && !HasTaskDefinitionType(element) && !HasMessageRef(element):
                throw Failure("bpmn.message_throw.target_required", elementId, "messageRef",
                    $"The sending element '{elementId}' requires messageRef or zeebe:taskDefinition/@type.");
            case "startEvent" or "intermediateCatchEvent" or "boundaryEvent" when HasEventDefinition(element, "timerEventDefinition")
                && !HasTimerSchedule(element):
                throw Failure("bpmn.timer.definition_required", elementId, "timerEventDefinition",
                    $"The timer event '{elementId}' requires timeCycle, timeDate, or timeDuration.");
            // Ein Error-Boundary faengt laut BPMN 2.0 immer unterbrechend. cancelActivity="false"
            // waere ein stilles Fehlverhalten und wird deshalb vor der Veroeffentlichung abgelehnt.
            case "boundaryEvent" when HasEventDefinition(element, "errorEventDefinition")
                && string.Equals(element.Attribute("cancelActivity")?.Value, "false", StringComparison.OrdinalIgnoreCase):
                throw Failure("bpmn.error_boundary.cancel_activity_invalid", elementId, "cancelActivity",
                    $"The error boundary event '{elementId}' must be interrupting; cancelActivity=\"false\" is not allowed.");
        }
    }

    /// <summary>
    /// Eine Call Activity muss wissen, welchen Prozess sie startet. Der Zielprozess wird hier
    /// bewusst <b>nicht</b> gesucht: Er darf spaeter deployt werden, und erst die Laufzeit
    /// entscheidet, welche Version dann aktuell ist.
    ///
    /// Die Prozesskennung bleibt in dieser Stufe ein Literal. Ein FEEL-Ausdruck waere erst zur
    /// Laufzeit bekannt; eine Veroeffentlichung koennte dann nicht mehr zusagen, welche
    /// Prozesse ein Workflow ueberhaupt aufruft.
    /// </summary>
    private static void ValidateCallActivity(XElement element, string elementId)
    {
        var processId = element.Descendants()
            .FirstOrDefault(descendant => descendant.Name.LocalName == "calledElement")
            ?.Attribute("processId")?.Value;

        if (string.IsNullOrWhiteSpace(processId))
        {
            throw Failure("bpmn.call_activity.process_id_required", elementId,
                "extensionElements.calledElement.processId",
                $"The call activity '{elementId}' requires zeebe:calledElement/@processId.");
        }

        if (processId.TrimStart().StartsWith('='))
        {
            throw Failure("bpmn.call_activity.process_id_literal_required", elementId,
                "extensionElements.calledElement.processId",
                $"The call activity '{elementId}' requires a literal process id; FEEL expressions are not supported yet.");
        }
    }

    /// <summary>
    /// Ein Business-Rule-Task hat zwei zulaessige Auspraegungen. Traegt er einen Auftragstyp,
    /// ist er ein gewoehnlicher Auftrag an einen Worker und braucht nichts weiter — die
    /// Entscheidung faellt dann ausserhalb.
    ///
    /// Sonst rechnet die Entscheidungstabelle lokal, und dafuer braucht es beides: die Decision,
    /// die gerechnet wird, und den Namen, unter dem ihr Ergebnis im Prozess landet. Ohne den
    /// Namen liefe die Tabelle, aber niemand koennte das Ergebnis lesen.
    /// </summary>
    private static void ValidateBusinessRuleTask(XElement element, string elementId)
    {
        if (HasTaskDefinitionType(element))
        {
            return;
        }

        var calledDecision = element.Descendants()
            .FirstOrDefault(descendant => descendant.Name.LocalName == "calledDecision");

        if (string.IsNullOrWhiteSpace(calledDecision?.Attribute("decisionId")?.Value))
        {
            throw Failure("bpmn.business_rule_task.decision_required", elementId,
                "extensionElements.calledDecision.decisionId",
                $"The business rule task '{elementId}' requires zeebe:calledDecision/@decisionId "
                + "or zeebe:taskDefinition/@type.");
        }

        if (string.IsNullOrWhiteSpace(calledDecision.Attribute("resultVariable")?.Value))
        {
            throw Failure("bpmn.business_rule_task.result_variable_required", elementId,
                "extensionElements.calledDecision.resultVariable",
                $"The business rule task '{elementId}' requires zeebe:calledDecision/@resultVariable.");
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

    /// <summary>
    /// Ein Element, das eine Nachricht aussendet: der Send-Task selbst oder ein werfendes
    /// Ereignis mit Nachrichtendefinition.
    /// </summary>
    private static bool IsMessageThrow(XElement element) => element.Name.LocalName == "sendTask"
        || HasEventDefinition(element, "messageEventDefinition");

    /// <summary>
    /// Der Verweis auf die <c>bpmn:message</c>. Er steht am Send-Task selbst, an einem Ereignis
    /// dagegen an seiner Nachrichtendefinition.
    /// </summary>
    private static bool HasMessageRef(XElement element) =>
        !string.IsNullOrWhiteSpace(element.Attribute("messageRef")?.Value)
        || element.Descendants().Any(descendant => descendant.Name.LocalName == "messageEventDefinition"
            && !string.IsNullOrWhiteSpace(descendant.Attribute("messageRef")?.Value));

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
