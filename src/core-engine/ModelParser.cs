using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;
using BPMN.Foundation;
using core_engine.Exceptions;
using Task = BPMN.Activities.Task;

namespace core_engine;

public static class ModelParser
{
    private static readonly XNamespace FlowzerExtensionNamespace = "https://flowzer.io/schema/bpmn/1.0";
    private static readonly XNamespace BpmnNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace ZeebeExtensionNamespace = "http://camunda.org/schema/zeebe/1.0";
    private const int MaximumDirectoryCandidatesPerKind = 100;

    /// <summary>
    /// Parse a FlowzerBPMN model from a stream
    /// </summary>
    /// <param name="stream">The stream to read the model from</param>
    /// <returns>The parsed FlowzerBPMN model</returns>
    public static async Task<Definitions> ParseModel(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var xml = await reader.ReadToEndAsync();
        return ParseModel(xml);
    }


    /// <summary>
    /// Parse a FlowzerBPMN model from a string
    /// </summary>
    /// <param name="xml">The string to read the model from</param>
    /// <returns>The parsed FlowzerBPMN model</returns>
    public static Definitions ParseModel(string xml)
    {
        var xDocument = XDocument.Parse(xml);
        var root = xDocument.Root!;
        var definitionsId = RequireId(root);

        FlowzerList<IRootElement> rootElements = [];

        rootElements.AddRange(ParseMessages(root));
        rootElements.AddRange(ParseSignals(root));
        rootElements.AddRange(ParseErrors(root));
        rootElements.AddRange(ParseProcesses(root, rootElements, definitionsId));

        var definitions = new Definitions
        {
            Id = definitionsId,
            RootElements = rootElements
        };

        return definitions;
    }

    /// <summary>
    /// Liest die <c>bpmn:message</c>-Wurzelelemente. Der <c>name</c> ist in BPMN 2.0
    /// <b>optional</b>; ein fremdes Dokument ohne Namen darf den Parser deshalb nicht zu Fall
    /// bringen. Ein Ereignis, das auf eine namenlose Nachricht zeigt, koennte allerdings nie
    /// korrelieren — das beanstandet die Veroeffentlichungspruefung mit
    /// <c>bpmn.message.name_required</c> am Ereignis.
    /// </summary>
    private static IEnumerable<MessageDefinition> ParseMessages(XElement root)
    {
        return root.Elements().Where(n =>
                n.Name.LocalName.Equals("message", StringComparison.InvariantCultureIgnoreCase))
            .Select(m => new MessageDefinition
            {
                Name = m.Attribute("name")?.Value ?? "",
                FlowzerId = m.Attribute("id")?.Value,
                FlowzerCorrelationKey = m.Descendants()
                    .FirstOrDefault(s => s.Name.LocalName == "subscription")?
                    .Attribute("correlationKey")?.Value,
            });
    }

    /// <summary>
    /// Liest die <c>bpmn:signal</c>-Wurzelelemente. Wie bei der Nachricht ist der <c>name</c>
    /// laut BPMN optional und wird deshalb leer gelesen statt geworfen.
    /// </summary>
    private static IEnumerable<Signal> ParseSignals(XElement root)
    {
        return root.Elements().Where(n =>
                n.Name.LocalName.Equals("signal", StringComparison.InvariantCultureIgnoreCase))
            .Select(m => new Signal()
            {
                Name = m.Attribute("name")?.Value ?? "",
                FlowzerId = m.Attribute("id")?.Value,
            });
    }

    /// <summary>
    /// Die Kennung eines BPMN-Elements. Sie bleibt Pflicht — die Engine verweist ueber sie auf
    /// jeden Knoten —, aber ein fremdes Dokument ohne Kennung bekommt den benannten Modellfehler
    /// mit der Elementart statt einer <c>NullReferenceException</c>.
    /// </summary>
    private static string RequireId(XElement element)
    {
        var id = element.Attribute("id")?.Value;

        return string.IsNullOrWhiteSpace(id)
            ? throw new ModelValidationException(
                $"The '{element.Name.LocalName}' element requires a non-empty id.")
            : id;
    }

    /// <summary>
    /// Ein Element, das Flowzer nicht ausfuehren kann. Gemeldet wird die Elementart samt
    /// Ereignisdefinition und die Kennung — nicht der rohe XML-Name mit Namensraum. Reines
    /// Diagramm-Beiwerk landet hier nie; es wird ueberlesen (<see cref="BpmnDiagramDecorations"/>).
    /// </summary>
    private static ModelValidationException UnsupportedElement(XElement element)
    {
        var eventDefinition = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal));
        var kind = eventDefinition is null
            ? element.Name.LocalName
            : $"{element.Name.LocalName}.{eventDefinition.Name.LocalName}";

        return new ModelValidationException(
            $"The BPMN element '{kind}' (id '{element.Attribute("id")?.Value ?? "(unknown)"}') is not supported.");
    }

    /// <summary>
    /// Loest ein <c>signalRef</c> auf. Ein Verweis ins Leere ist ein Modellfehler und wird
    /// benannt, statt als <c>InvalidOperationException</c> aus einer LINQ-Abfrage zu entgleiten.
    /// </summary>
    private static Signal RequireSignal(IEnumerable<IRootElement> rootElements, XElement definition, string elementId)
    {
        var signalRef = definition.Attribute("signalRef")?.Value;

        return rootElements.OfType<Signal>().FirstOrDefault(signal => signal.FlowzerId == signalRef)
               ?? throw new ModelValidationException(
                   $"The event '{elementId}' references the unknown signal '{signalRef ?? "(none)"}'.");
    }

    /// <summary>
    /// Loest ein <c>messageRef</c> an einem Element auf, das ohne Nachricht keinen Sinn ergibt
    /// (fangende Ereignisse, Empfangsaufgabe). Fehlender oder unbekannter Verweis ist ein
    /// benannter Modellfehler.
    /// </summary>
    private static MessageDefinition RequireMessage(
        IEnumerable<IRootElement> rootElements, string? messageRef, string elementId)
    {
        return rootElements.OfType<MessageDefinition>().FirstOrDefault(message => message.FlowzerId == messageRef)
               ?? throw new ModelValidationException(
                   $"The element '{elementId}' references the unknown message '{messageRef ?? "(none)"}'.");
    }

    /// <summary>
    /// Liest die <c>bpmn:error</c>-Wurzelelemente. Sie stehen neben den Prozessen im Dokument;
    /// ein Error-End- oder Error-Boundary-Event zeigt ueber <c>errorRef</c> darauf.
    /// </summary>
    private static IEnumerable<Error> ParseErrors(XElement root)
    {
        return root.Elements().Where(n =>
                n.Name.LocalName.Equals("error", StringComparison.InvariantCultureIgnoreCase))
            .Select(m => new Error
            {
                Name = m.Attribute("name")?.Value ?? "",
                ErrorCode = m.Attribute("errorCode")?.Value,
                FlowzerId = m.Attribute("id")?.Value,
            });
    }

    /// <summary>
    /// Loest ein <c>errorRef</c> auf. Ein fehlendes Attribut ist zulaessig (der Fehler traegt dann
    /// keinen Code); ein Verweis ins Leere ist dagegen ein Modellfehler und wird benannt.
    /// </summary>
    private static Error? ResolveErrorRef(XElement definition, List<IRootElement> rootElements, string elementId)
    {
        var errorRef = definition.Attribute("errorRef")?.Value;
        if (string.IsNullOrWhiteSpace(errorRef))
        {
            return null;
        }

        return rootElements.OfType<Error>().FirstOrDefault(error => error.FlowzerId == errorRef)
               ?? throw new ModelValidationException(
                   $"The error event '{elementId}' references the unknown error '{errorRef}'.");
    }

    private static List<Process> ParseProcesses(XElement root, List<IRootElement> rootElements,
        string definitionsId)
    {
        // Gib mir alle Nodes unter root, die vom Typ "bpmn:process" sind

        FlowzerList<Process> processes = [];

        var xmlProcessNodes = root.Elements().Where(n =>
            n.Name.LocalName.Equals("process", StringComparison.InvariantCultureIgnoreCase) &&
            string.Equals((string?)n.Attribute("isExecutable"), "true", StringComparison.InvariantCultureIgnoreCase));

        processes.AddRange(
            xmlProcessNodes.Select(xmlProcessNode => new Process
            {
                Id = RequireId(xmlProcessNode),
                Name = xmlProcessNode.Attribute("name")?.Value,
                IsExecutable = true,
                FlowElements = GetFlowElements(rootElements, xmlProcessNode),
                FlowzerUserTaskForms = ParseUserTaskForms(xmlProcessNode),
                DefinitionsId = definitionsId,
            }));

        return processes;
    }

    /// <summary>
    /// Liest nur die eingebetteten Formulare eines BPMN-Dokuments, ohne das ganze Modell zu
    /// bauen. Fuer den Formularabruf zur Laufzeit ist das der richtige Schnitt: Ein Fehler an
    /// einer ganz anderen Stelle des Modells darf nicht verhindern, dass eine wartende Aufgabe
    /// ihr Formular bekommt. Die Regel, was als Formular zaehlt, bleibt dieselbe wie beim
    /// vollstaendigen Parsen.
    /// </summary>
    public static FlowzerList<FlowzerUserTaskForm> ParseUserTaskForms(string xml)
    {
        var root = XDocument.Parse(xml).Root;

        return root is null
            ? []
            : root.Elements()
                .Where(node => node.Name.LocalName.Equals("process", StringComparison.InvariantCultureIgnoreCase))
                .SelectMany(ParseUserTaskForms)
                .ToFlowzerList();
    }

    /// <summary>
    /// Liest die Formulare, die der Workflow selbst mitbringt. Sie stehen als
    /// <c>zeebe:userTaskForm</c> direkt in den <c>extensionElements</c> des Prozesses — bewusst
    /// nur dort: Ein gleichnamiges Element an einer Aufgabe waere kein Prozessformular, und ein
    /// Form-Key duerfte nicht darauf zeigen.
    /// </summary>
    private static FlowzerList<FlowzerUserTaskForm> ParseUserTaskForms(XElement xmlProcessNode)
    {
        return xmlProcessNode.Elements()
            .Where(element => element.Name.LocalName == "extensionElements")
            .Elements()
            .Where(element => element.Name.LocalName == "userTaskForm")
            .Select(element => new FlowzerUserTaskForm(
                element.Attribute("id")?.Value?.Trim() ?? "",
                element.Value))
            .Where(form => form.Id.Length > 0)
            .ToFlowzerList();
    }

    private static FlowzerList<FlowElement> GetFlowElements(List<IRootElement> rootElements, XElement xmlProcessNode)
    {
        FlowzerList<FlowElement> flowElements = [];

        foreach (var xmlFlowNode in xmlProcessNode.Elements())
        {
            var inputMappings = ParseIoMappings(xmlFlowNode, "input");
            var outputMappings = ParseIoMappings(xmlFlowNode, "output");

            switch (xmlFlowNode.Name.LocalName)
            {
                case "startEvent":
                    flowElements.Add(HandleStartEvent(xmlFlowNode, rootElements, outputMappings));
                    break;

                case "intermediateCatchEvent":
                case "intermediateThrowEvent":
                    flowElements.Add(HandleIntermediateEvent(xmlFlowNode, rootElements,
                        xmlFlowNode.Name.LocalName, inputMappings, outputMappings));
                    break;

                case "serviceTask":
                    flowElements.Add(HandleServiceTask(xmlFlowNode, inputMappings, outputMappings));
                    break;

                case "userTask":
                    flowElements.Add(HandleUserTask(xmlFlowNode, inputMappings, outputMappings));
                    break;

                case "scriptTask":
                    flowElements.Add(HandleScriptTask(xmlFlowNode, inputMappings, outputMappings));
                    break;

                case "subProcess":
                    flowElements.Add(new SubProcess
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        InputMappings = inputMappings,
                        OutputMappings = outputMappings,
                        FlowElements = GetFlowElements(rootElements, xmlFlowNode),
                        LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
                    });
                    break;

                case "receiveTask":
                    flowElements.Add(new ReceiveTask
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        MessageRef = RequireMessage(rootElements, xmlFlowNode.Attribute("messageRef")?.Value,
                            RequireId(xmlFlowNode)),
                        LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
                    });
                    break;

                case "sendTask":
                    flowElements.Add(new SendTask
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        Implementation = ReadTaskDefinitionType(xmlFlowNode),
                        FlowzerRetries = ParseRetries(FindTaskDefinition(xmlFlowNode)),
                        MessageRef = ResolveMessageRef(rootElements, xmlFlowNode.Attribute("messageRef")?.Value),
                        InputMappings = inputMappings,
                        LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
                    });
                    break;

                case "callActivity":
                    flowElements.Add(HandleCallActivity(xmlFlowNode, inputMappings, outputMappings));
                    break;

                case "task":
                    flowElements.Add(new Task
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
                    });
                    break;

                case "manualTask":
                    flowElements.Add(new ManualTask
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
                    });
                    break;

                case "exclusiveGateway":
                    flowElements.Add(new ExclusiveGateway
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        DefaultId = xmlFlowNode.Attribute("default")?.Value,
                    });
                    break;

                case "complexGateway":
                    flowElements.Add(new ComplexGateway()
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        DefaultId = xmlFlowNode.Attribute("default")?.Value,
                    });
                    break;

                case "inclusiveGateway":
                    flowElements.Add(new InclusiveGateway()
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        DefaultId = xmlFlowNode.Attribute("default")?.Value,
                    });
                    break;

                case "parallelGateway":
                    flowElements.Add(new ParallelGateway
                    {
                        Id = RequireId(xmlFlowNode),
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    });
                    break;

                case "endEvent":
                    flowElements.Add(HandleEndEvent(xmlFlowNode, rootElements, inputMappings));
                    break;

                case "extensionElements":
                case "sequenceFlow":
                case "boundaryEvent":
                case "incoming":
                case "outgoing":
                case "multiInstanceLoopCharacteristics":
                    break;

                default:
                    // Reines Diagramm-Beiwerk — Lanes, Beschriftungen, Datenobjekte — darf laut
                    // BPMN 2.0 ueberall stehen und traegt keine Ausfuehrungssemantik. Es wird
                    // ueberlesen; gemeldet wird nur eine echte Ausfuehrungsluecke.
                    if (BpmnDiagramDecorations.Contains(xmlFlowNode.Name.LocalName))
                    {
                        break;
                    }

                    throw UnsupportedElement(xmlFlowNode);
            }
        }

        foreach (var xmlFlowNode in xmlProcessNode.Elements().Where(x => x.Name.LocalName == "boundaryEvent"))
        {
            var attachedTo = RequireAttachedActivity(xmlFlowNode, flowElements);
            if (!bool.TryParse(xmlFlowNode.Attribute("cancelActivity")?.Value, out var cancelActivity))
                cancelActivity = true;
            if (xmlFlowNode.HasDescendant("messageEventDefinition", out var definition))
            {
                flowElements.Add(new FlowzerBoundaryMessageEvent
                {
                    Id = RequireId(xmlFlowNode),
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    MessageDefinition = RequireMessage(rootElements, definition.Attribute("messageRef")?.Value,
                        RequireId(xmlFlowNode)),
                    AttachedToRef = attachedTo,
                    CancelActivity = cancelActivity
                });
                continue;
            }

            if (xmlFlowNode.HasDescendant("signalEventDefinition", out definition))
            {
                flowElements.Add(new FlowzerBoundarySignalEvent()
                {
                    Id = RequireId(xmlFlowNode),
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    Signal = RequireSignal(rootElements, definition, RequireId(xmlFlowNode)),
                    AttachedToRef = attachedTo,
                    CancelActivity = cancelActivity
                });
                continue;
            }

            if (xmlFlowNode.HasDescendant("timerEventDefinition", out definition))
            {
                flowElements.Add(new FlowzerBoundaryTimerEvent
                {
                    Id = RequireId(xmlFlowNode),
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    TimerDefinition = ParseTimerEventDefinition(definition),
                    AttachedToRef = attachedTo,
                    CancelActivity = cancelActivity
                });
                continue;
            }

            if (xmlFlowNode.HasDescendant("errorEventDefinition", out definition))
            {
                var boundaryErrorId = RequireId(xmlFlowNode);
                flowElements.Add(new FlowzerBoundaryErrorEvent
                {
                    Id = boundaryErrorId,
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    Error = ResolveErrorRef(definition, rootElements, boundaryErrorId),
                    AttachedToRef = attachedTo,
                    // Ein Error-Boundary unterbricht laut BPMN-Spezifikation immer. Die
                    // Veroeffentlichungspruefung lehnt cancelActivity="false" bereits ab.
                    CancelActivity = true
                });
                continue;
            }

            throw UnsupportedElement(xmlFlowNode);
        }

        foreach (var xmlFlowNode in xmlProcessNode.Elements().Where(x => x.Name.LocalName == "sequenceFlow"))
        {
            var sequenceFlowId = RequireId(xmlFlowNode);
            var source = RequireFlowNode(xmlFlowNode, "sourceRef", flowElements, sequenceFlowId);
            var target = RequireFlowNode(xmlFlowNode, "targetRef", flowElements, sequenceFlowId);
            var isDefault = source.GetType().GetInterfaces().Contains(typeof(IHasDefault))
                            && ((IHasDefault)source).DefaultId == sequenceFlowId;
            var newSequenceFlow = new SequenceFlow
            {
                Id = sequenceFlowId,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                SourceRef = source,
                TargetRef = target,
                FlowzerIsDefault = isDefault,
                // Container = process,
                FlowzerCondition = xmlFlowNode.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "conditionExpression")
                    ?.Value,
            };

            flowElements.Add(newSequenceFlow);
        }

        return flowElements;
    }

    /// <summary>
    /// Die Aktivitaet, an der ein Boundary-Event haengt. Ein fehlendes oder ins Leere zeigendes
    /// <c>attachedToRef</c> ist ein Modellfehler mit Namen, keine Nullreferenz und kein
    /// misslungener Typumwandlungsversuch.
    /// </summary>
    private static Activity RequireAttachedActivity(XElement xmlFlowNode, IEnumerable<FlowElement> flowElements)
    {
        var boundaryId = RequireId(xmlFlowNode);
        var attachedToRef = xmlFlowNode.Attribute("attachedToRef")?.Value;
        var attachedTo = flowElements.FirstOrDefault(element => element.Id == attachedToRef);

        return attachedTo as Activity
               ?? throw new ModelValidationException(
                   $"The boundary event '{boundaryId}' must be attached to an activity of the same container; "
                   + $"'{attachedToRef ?? "(none)"}' is not one.");
    }

    /// <summary>
    /// Quelle oder Ziel eines Sequenzflusses. Ein Verweis ins Leere — oder auf etwas, das kein
    /// Knoten ist — wird benannt, statt als Ausnahme aus LINQ oder aus einer Typumwandlung zu kommen.
    /// </summary>
    private static FlowNode RequireFlowNode(
        XElement xmlFlowNode, string attributeName, IEnumerable<FlowElement> flowElements, string sequenceFlowId)
    {
        var reference = xmlFlowNode.Attribute(attributeName)?.Value;
        var referenced = flowElements.FirstOrDefault(element => element.Id == reference);

        return referenced as FlowNode
               ?? throw new ModelValidationException(
                   $"The sequence flow '{sequenceFlowId}' references the unknown {attributeName} "
                   + $"'{reference ?? "(none)"}'.");
    }

    /// <summary>
    /// Eine Aufruf-Aktivitaet. Ohne <c>zeebe:calledElement/@processId</c> wuesste die Laufzeit
    /// nicht, was sie starten soll; das ist ein benannter Modellfehler. Die beiden
    /// Weitergabeschalter bleiben bei unlesbarem Wert auf ihrem Vorgabewert <c>true</c>, statt
    /// das ganze Dokument an einem Schreibfehler scheitern zu lassen.
    /// </summary>
    private static CallActivity HandleCallActivity(
        XElement xmlFlowNode,
        FlowzerList<FlowzerIoMapping>? inputMappings,
        FlowzerList<FlowzerIoMapping>? outputMappings)
    {
        var callActivityId = RequireId(xmlFlowNode);
        var calledElement = xmlFlowNode.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "calledElement");
        var processId = calledElement?.Attribute("processId")?.Value;
        if (string.IsNullOrWhiteSpace(processId))
        {
            throw new ModelValidationException(
                $"The call activity '{callActivityId}' requires zeebe:calledElement/@processId.");
        }

        return new CallActivity
        {
            Id = callActivityId,
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            DefaultId = xmlFlowNode.Attribute("default")?.Value,
            FlowzerCalledElementProcessId = processId,
            FlowzerPropagateAllChildVariables = ReadFlag(calledElement, "propagateAllChildVariables"),
            FlowzerPropagateAllParentVariables = ReadFlag(calledElement, "propagateAllParentVariables"),
            InputMappings = inputMappings,
            OutputMappings = outputMappings,
            LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
        };
    }

    /// <summary>Ein optionaler Ja/Nein-Schalter; fehlend oder unlesbar heisst <c>true</c>.</summary>
    private static bool ReadFlag(XElement? element, string attributeName) =>
        !bool.TryParse(element?.Attribute(attributeName)?.Value, out var value) || value;

    private static EndEvent HandleEndEvent(XElement xmlFlowNode, List<IRootElement> rootElements,
        FlowzerList<FlowzerIoMapping>? inputMappings = null)
    {
        if (xmlFlowNode.HasDescendant("terminateEventDefinition", out _))
        {
            return new FlowzerTerminateEvent
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            };
        }

        if (xmlFlowNode.HasDescendant("messageEventDefinition", out var definition))
        {
            return new FlowzerMessageEndEvent
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                Implementation = ReadTaskDefinitionType(xmlFlowNode),
                FlowzerRetries = ParseRetries(FindTaskDefinition(xmlFlowNode)),
                MessageDefinition = ResolveMessageRef(rootElements, definition.Attribute("messageRef")?.Value),
                InputMappings = inputMappings,
            };
        }

        if (xmlFlowNode.HasDescendant("signalEventDefinition", out definition))
        {
            return new FlowzerSignalEndEvent()
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                Signal = RequireSignal(rootElements, definition, RequireId(xmlFlowNode)),
            };
        }

        if (xmlFlowNode.HasDescendant("errorEventDefinition", out definition))
        {
            var errorEndId = RequireId(xmlFlowNode);
            return new FlowzerErrorEndEvent
            {
                Id = errorEndId,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                Error = ResolveErrorRef(definition, rootElements, errorEndId),
            };
        }

        return new EndEvent
        {
            Id = RequireId(xmlFlowNode),
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
        };
    }

    private static Event HandleIntermediateEvent(XElement xmlFlowNode, List<IRootElement> rootElements,
        string name, FlowzerList<FlowzerIoMapping>? inputMappings = null,
        FlowzerList<FlowzerIoMapping>? outputMappings = null)
    {
        if (xmlFlowNode.HasDescendant("timerEventDefinition", out var definition))
        {
            return new FlowzerIntermediateTimerCatchEvent()
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                TimerDefinition = ParseTimerEventDefinition(definition),
            };
        }

        var catchEvent = name.Contains("catch", StringComparison.InvariantCultureIgnoreCase);
        if (xmlFlowNode.HasDescendant("messageEventDefinition", out definition))
        {
            return catchEvent
                ? new FlowzerIntermediateMessageCatchEvent()
                {
                    Id = RequireId(xmlFlowNode),
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    MessageDefinition = RequireMessage(rootElements, definition.Attribute("messageRef")?.Value, RequireId(xmlFlowNode)),
                    OutputMappings = outputMappings,
                }
                : new FlowzerIntermediateMessageThrowEvent()
                {
                    Id = RequireId(xmlFlowNode),
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    Implementation = ReadTaskDefinitionType(xmlFlowNode),
                    FlowzerRetries = ParseRetries(FindTaskDefinition(xmlFlowNode)),
                    MessageDefinition = ResolveMessageRef(rootElements, definition.Attribute("messageRef")?.Value),
                    InputMappings = inputMappings,
                };
        }

        if (xmlFlowNode.HasDescendant("signalEventDefinition", out definition))
        {
            return catchEvent
                ? new FlowzerIntermediateSignalCatchEvent()
                {
                    Id = RequireId(xmlFlowNode),
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    Signal = RequireSignal(rootElements, definition, RequireId(xmlFlowNode)),
                }
                : new FlowzerIntermediateSignalThrowEvent()
                {
                    Id = RequireId(xmlFlowNode),
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    Signal = RequireSignal(rootElements, definition, RequireId(xmlFlowNode)),
                };
        }

        if (catchEvent)
            return new IntermediateCatchEvent()
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            };
        return new IntermediateThrowEvent()
        {
            Id = RequireId(xmlFlowNode),
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
        };
    }

    private static ServiceTask HandleServiceTask(XElement xmlFlowNode, FlowzerList<FlowzerIoMapping>? inputMappings,
        FlowzerList<FlowzerIoMapping>? outputMappings)
    {
        var taskDefinition = FindTaskDefinition(xmlFlowNode);

        return new ServiceTask
        {
            Id = RequireId(xmlFlowNode),
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            // Container = process,
            DefaultId = xmlFlowNode.Attribute("default")?.Value,
            Implementation =
                xmlFlowNode.Attribute("Implementation")?.Value
                ?? taskDefinition?.Attribute("type")?.Value
                ?? throw new ModelValidationException(
                    $"Implementation not defined for Service task '{RequireId(xmlFlowNode)}'"),
            FlowzerRetries = ParseRetries(taskDefinition),
            FlowzerAiTask = AiTaskContractParser.Parse(xmlFlowNode),
            InputMappings = inputMappings,
            OutputMappings = outputMappings,
            LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
        };
    }

    private static XElement? FindTaskDefinition(XElement xmlFlowNode) => xmlFlowNode.Descendants()
        .FirstOrDefault(e => e.Name.LocalName == "taskDefinition");

    /// <summary>
    /// Der Auftragstyp aus <c>zeebe:taskDefinition/@type</c>. Leer heißt an einem sendenden
    /// Element: Die Engine korreliert die Nachricht selbst, statt einen Worker zu beauftragen.
    /// </summary>
    private static string ReadTaskDefinitionType(XElement xmlFlowNode) =>
        FindTaskDefinition(xmlFlowNode)?.Attribute("type")?.Value ?? "";

    /// <summary>
    /// Loest ein <c>messageRef</c> auf. Ein fehlendes Attribut ist zulaessig — ein sendendes
    /// Element kann stattdessen einen Auftragstyp tragen. Ein Verweis ins Leere ist dagegen ein
    /// Modellfehler und wird benannt, statt als „keine Nachricht" durchzurutschen.
    /// </summary>
    private static MessageDefinition? ResolveMessageRef(List<IRootElement> rootElements, string? messageRef)
    {
        if (string.IsNullOrWhiteSpace(messageRef))
        {
            return null;
        }

        return rootElements.OfType<MessageDefinition>().FirstOrDefault(m => m.FlowzerId == messageRef)
               ?? throw new ModelValidationException($"The unknown message '{messageRef}' is referenced.");
    }

    /// <summary>
    /// Die Wiederholungen aus <c>zeebe:taskDefinition/@retries</c>. Zeebe erlaubt dort auch einen
    /// FEEL-Ausdruck; der laesst sich beim Parsen nicht aufloesen, deshalb bleibt es dann beim
    /// Standardwert 0 — die Auftragsvergabe setzt darauf ihren eigenen Vorgabewert.
    /// </summary>
    private static int ParseRetries(XElement? taskDefinition)
    {
        var retries = taskDefinition?.Attribute("retries")?.Value;

        return int.TryParse(retries, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;
    }

    private static LoopCharacteristics? ParseLoopCharacteristics(XElement xmlFlowNode)
    {
        var loopCharacteristicsXmlNode = xmlFlowNode.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "multiInstanceLoopCharacteristics");
        if (loopCharacteristicsXmlNode is null) return null;

        var isSequential = loopCharacteristicsXmlNode.Attribute("isSequential")?.Value == "true";

        return new MultiInstanceLoopCharacteristics
        {
            IsSequential = isSequential,
            CompletionCondition = ParseExpression(loopCharacteristicsXmlNode, "completionCondition"),
            FlowzerLoopCharacteristics = ParseFlowzerLoopCharacteristics(loopCharacteristicsXmlNode),
        };
    }

    private static BPMN.Common.Expression? ParseExpression(XElement loopCharacteristicsXmlNode, string xmlNodeName)
    {
        var element = loopCharacteristicsXmlNode.Descendants().FirstOrDefault(x => x.Name.LocalName == xmlNodeName);
        if (element != null)
        {
            return new BPMN.Common.Expression
            {
                Body = element.Value,
            };
        }

        return null;
    }

    private static FlowzwerLoopCharacteristics? ParseFlowzerLoopCharacteristics(XElement loopCharacteristicsXmlNode)
    {
        if (!(loopCharacteristicsXmlNode.HasDescendant("extensionElements", out var extensionElements)))
            return null;

        if (!(extensionElements.HasDescendant("loopCharacteristics", out var loopCharacteristicsNode)))
            return null;


        var varInputCollection = loopCharacteristicsNode.Attribute("inputCollection")?.Value;
        if (varInputCollection == null)
            throw new ModelValidationException("InputCollection must be set on loopCharacteristicsXmlNode");

        return new FlowzwerLoopCharacteristics()
        {
            InputCollection = varInputCollection,
            InputElement = loopCharacteristicsNode.Attribute("inputElement")?.Value,
            OutputCollection = loopCharacteristicsNode.Attribute("outputCollection")?.Value,
            OutputElement = loopCharacteristicsNode.Attribute("outputElement")?.Value,
        };
    }

    private static FlowzerScriptTask HandleScriptTask(XElement xmlFlowNode,
        FlowzerList<FlowzerIoMapping>? inputMappings,
        FlowzerList<FlowzerIoMapping>? outputMappings)
    {
        var script = xmlFlowNode.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "script");

        return new FlowzerScriptTask
        {
            Id = RequireId(xmlFlowNode),
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            // Container = process,
            DefaultId = xmlFlowNode.Attribute("default")?.Value,
            Type = script is not null
                ? FlowzerScriptTaskType.Script
                : FlowzerScriptTaskType.Service,
            Implementation =
                xmlFlowNode.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "taskDefinition")
                    ?.Attribute("type")
                    ?.Value,
            ResultVar = script?.Attribute("resultVariable")
                ?.Value,
            InputMappings = inputMappings,
            OutputMappings = outputMappings,
            ScriptFormat = script is null ? "Service" : "FEEL",
            Script = script?.Attribute("expression")?.Value,
            LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
        };
    }

    private static UserTask HandleUserTask(XElement xmlFlowNode, FlowzerList<FlowzerIoMapping>? inputMappings,
        FlowzerList<FlowzerIoMapping>? outputMappings)
    {
        var formDefinition = xmlFlowNode.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "formDefinition");
        var assignmentDefinition = xmlFlowNode.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "assignmentDefinition");
        var assignment = ParseUserTaskAssignment(xmlFlowNode, assignmentDefinition);
        var taskSchedule = xmlFlowNode.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "taskSchedule");
        return new UserTask
        {
            Id = RequireId(xmlFlowNode),
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            // Container = process,
            DefaultId = xmlFlowNode.Attribute("default")?.Value,
            Implementation = GetUserTaskImplementation(xmlFlowNode, ReadFormKey(formDefinition)),
            FlowzerAssignee = assignmentDefinition?.Attribute("assignee")?.Value,
            FlowzerCandidateGroups = assignmentDefinition?.Attribute("candidateGroups")?.Value,
            FlowzerCandidateUsers = assignmentDefinition?.Attribute("candidateUsers")?.Value,
            FlowzerAssignmentMode = assignment.Mode,
            FlowzerDirectoryAssigneeUserId = assignment.AssigneeUserId,
            FlowzerDirectoryCandidateUserIds = assignment.CandidateUserIds.ToFlowzerList(),
            FlowzerDirectoryCandidateGroupIds = assignment.CandidateGroupIds.ToFlowzerList(),
            FlowzerDueDate = taskSchedule?.Attribute("dueDate")?.Value,
            FlowzerFollowUpDate = taskSchedule?.Attribute("followUpDate")?.Value,
            InputMappings = inputMappings,
            OutputMappings = outputMappings,
            LoopCharacteristics = ParseLoopCharacteristics(xmlFlowNode),
        };
    }

    /// <summary>
    /// Liest ausschließlich die versionierte Flowzer-Erweiterung. Zeebe-Freitext bleibt der
    /// kompatible Standard; Directory-IDs und Freitext dürfen nie zu einem Mischvertrag werden.
    /// </summary>
    private static ParsedUserTaskAssignment ParseUserTaskAssignment(
        XElement userTask,
        XElement? legacyAssignment)
    {
        var assignmentElements = userTask.Descendants()
            .Where(element => element.Name.LocalName == "taskAssignment")
            .ToArray();
        if (assignmentElements.Length == 0)
        {
            return ParsedUserTaskAssignment.Text;
        }

        var taskId = userTask.Attribute("id")?.Value ?? "<unknown>";
        if (assignmentElements.Any(element => element.Name.Namespace != FlowzerExtensionNamespace))
        {
            throw AssignmentError(taskId, $"taskAssignment must use namespace '{FlowzerExtensionNamespace}'");
        }

        if (assignmentElements.Length != 1)
        {
            throw AssignmentError(taskId, "exactly one flowzer:taskAssignment is allowed");
        }

        var element = assignmentElements[0];
        var supportedAttributes = new HashSet<string>(StringComparer.Ordinal)
        {
            "mode", "assigneeId", "candidateUserIds", "candidateGroupIds"
        };
        if (element.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration
                && (attribute.Name.Namespace != XNamespace.None
                    || !supportedAttributes.Contains(attribute.Name.LocalName))))
        {
            throw AssignmentError(taskId, "taskAssignment contains an unsupported attribute");
        }

        var mode = element.Attribute("mode")?.Value;
        if (string.Equals(mode, "text", StringComparison.Ordinal))
        {
            if (element.Attribute("assigneeId") is not null
                || element.Attribute("candidateUserIds") is not null
                || element.Attribute("candidateGroupIds") is not null)
            {
                throw AssignmentError(taskId, "text mode cannot contain directory references");
            }

            return ParsedUserTaskAssignment.Text;
        }

        if (!string.Equals(mode, "directory", StringComparison.Ordinal))
        {
            throw AssignmentError(taskId, "mode must be 'text' or 'directory'");
        }

        if (HasLegacyAssignmentValues(legacyAssignment))
        {
            throw AssignmentError(taskId, "directory mode cannot be combined with Zeebe text assignments");
        }

        var assignee = ParseOptionalDirectoryId(element.Attribute("assigneeId"), taskId, "assigneeId");
        var candidateUsers = ParseDirectoryIds(element.Attribute("candidateUserIds"), taskId, "candidateUserIds");
        var candidateGroups = ParseDirectoryIds(element.Attribute("candidateGroupIds"), taskId, "candidateGroupIds");
        if (assignee is null && candidateUsers.Count == 0 && candidateGroups.Count == 0)
        {
            throw AssignmentError(taskId, "directory mode requires at least one reference");
        }

        if (assignee.HasValue && candidateUsers.Contains(assignee.Value))
        {
            throw AssignmentError(taskId, "the assignee must not also occur as candidate user");
        }

        return new ParsedUserTaskAssignment(
            UserTaskAssignmentMode.Directory,
            assignee,
            candidateUsers,
            candidateGroups);
    }

    private static bool HasLegacyAssignmentValues(XElement? legacyAssignment) =>
        legacyAssignment?.Attributes().Any(attribute =>
            attribute.Name.LocalName is "assignee" or "candidateUsers" or "candidateGroups"
            && !string.IsNullOrWhiteSpace(attribute.Value)) == true;

    private static Guid? ParseOptionalDirectoryId(XAttribute? attribute, string taskId, string field)
    {
        if (attribute is null) return null;
        if (!Guid.TryParse(attribute.Value, out var id) || id == Guid.Empty)
        {
            throw AssignmentError(taskId, $"{field} must be a non-empty UUID");
        }

        return id;
    }

    private static IReadOnlyList<Guid> ParseDirectoryIds(XAttribute? attribute, string taskId, string field)
    {
        if (attribute is null) return [];
        var values = attribute.Value.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length > MaximumDirectoryCandidatesPerKind)
        {
            throw AssignmentError(taskId, $"{field} exceeds {MaximumDirectoryCandidatesPerKind} entries");
        }

        var result = new List<Guid>(values.Length);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value, out var id) || id == Guid.Empty)
            {
                throw AssignmentError(taskId, $"{field} must contain only non-empty UUIDs");
            }

            if (result.Contains(id))
            {
                throw AssignmentError(taskId, $"{field} contains a duplicate reference");
            }

            result.Add(id);
        }

        return result;
    }

    private static ModelValidationException AssignmentError(string taskId, string reason) =>
        new($"User task '{taskId}' has an invalid assignment contract: {reason}.");

    private sealed record ParsedUserTaskAssignment(
        UserTaskAssignmentMode Mode,
        Guid? AssigneeUserId,
        IReadOnlyList<Guid> CandidateUserIds,
        IReadOnlyList<Guid> CandidateGroupIds)
    {
        public static ParsedUserTaskAssignment Text { get; } =
            new(UserTaskAssignmentMode.Text, null, [], []);
    }

    private static FlowzerList<FlowzerIoMapping>? ParseIoMappings(XElement xmlFlowNode, string mappingName)
    {
        // Keine rekursive LocalName-Suche: Fremde Erweiterungen und verschachtelte
        // Datenstrukturen sind keine ausführbaren Ein-/Ausgangszuordnungen.
        var mappings = xmlFlowNode.Elements(BpmnNamespace + "extensionElements")
            .SelectMany(extension => extension.Elements(ZeebeExtensionNamespace + "ioMapping"))
            .SelectMany(mapping => mapping.Elements(ZeebeExtensionNamespace + mappingName))
            .Select(element => new FlowzerIoMapping(
                RequireMappingValue(element, "source", xmlFlowNode),
                RequireMappingValue(element, "target", xmlFlowNode)))
            .ToFlowzerList();

        return mappings.Any()
            ? mappings
            : null;
    }

    /// <summary>
    /// Quelle beziehungsweise Ziel einer Ein-/Ausgangszuordnung. Beide sind in der
    /// Zeebe-Erweiterung Pflicht; eine unvollstaendige Zuordnung wird benannt gemeldet.
    /// </summary>
    private static string RequireMappingValue(XElement mapping, string attributeName, XElement xmlFlowNode)
    {
        var value = mapping.Attribute(attributeName)?.Value;

        return string.IsNullOrWhiteSpace(value)
            ? throw new ModelValidationException(
                $"The io mapping of '{xmlFlowNode.Attribute("id")?.Value ?? "(unknown)"}' requires "
                + $"a non-empty {attributeName}.")
            : value;
    }

    /// <summary>
    /// Der Formularverweis aus einem <c>zeebe:formDefinition</c>. <c>formId</c> gilt als Ersatz
    /// fuer <c>formKey</c>; ein leerer Wert heisst „kein Formular". Gemeinsam fuer User-Task und
    /// Startereignis, damit dasselbe Diagramm nicht je Elementart anders gelesen wird.
    /// </summary>
    private static string? ReadFormKey(XElement? formDefinition)
    {
        var formKey = formDefinition?.Attribute("formKey")?.Value
                      ?? formDefinition?.Attribute("formId")?.Value;

        return string.IsNullOrWhiteSpace(formKey) ? null : formKey.Trim();
    }

    private static string GetUserTaskImplementation(XElement xmlFlowNode, string? formKey)
    {
        if (formKey is not null)
        {
            return formKey;
        }

        throw new FlowzerModelParseException(
            $"User task '{xmlFlowNode.Attribute("id")?.Value ?? "(unknown)"}' requires either formKey or formId in formDefinition.");
    }

    /// <summary>
    /// Sucht eine Ereignis- oder Erweiterungsdefinition unterhalb eines Elements. Bewusst der
    /// <b>erste</b> Treffer: Ein Ereignis mit mehr als einer Ereignisdefinition ist ein
    /// Modellfehler, den die Veroeffentlichungspruefung mit <c>bpmn.event_definition.invalid</c>
    /// benennt — der Parser soll daran nicht mit einer LINQ-Ausnahme zerbrechen.
    /// </summary>
    private static bool HasDescendant(this XElement element, string name,
        [NotNullWhen(returnValue: true)] out XElement? descendant)
    {
        descendant = element.Descendants()
            .FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        return descendant != null;
    }

    private static StartEvent HandleStartEvent(XElement xmlFlowNode,
        IEnumerable<IRootElement> rootElements,
        FlowzerList<FlowzerIoMapping>? outputMappings)
    {
        StartEvent returnEvent;

        if (xmlFlowNode.HasDescendant("timerEventDefinition", out var definition))
        {
            returnEvent = new FlowzerTimerStartEvent
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                // Container = process,
                TimerDefinition = ParseTimerEventDefinition(definition),
                OutputMappings = outputMappings,
            };
        }

        else if (xmlFlowNode.HasDescendant("messageEventDefinition", out definition))
        {
            returnEvent = new FlowzerMessageStartEvent
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                // Container = process,
                MessageDefinition = RequireMessage(rootElements, definition.Attribute("messageRef")?.Value, RequireId(xmlFlowNode)),
                OutputMappings = outputMappings
            };
        }

        else if (xmlFlowNode.HasDescendant("signalEventDefinition", out definition))
        {
            returnEvent = new FlowzerSignalStartEvent
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                // Container = process,
                Signal = RequireSignal(rootElements, definition, RequireId(xmlFlowNode)),
                OutputMappings = outputMappings
            };
        }

        else
        {
            // Nur am reinen Startereignis: Ein Startformular fuellt aus, wer den Workflow von
            // Hand startet. An einem Timer-, Nachrichten- oder Signalstart wird ein
            // formDefinition bewusst still uebergangen statt als Modellfehler gemeldet —
            // bpmn-js behaelt die extensionElements beim Wechsel des Ereignistyps, und ein
            // Diagramm soll dadurch nicht unspeicherbar werden.
            returnEvent = new StartEvent
            {
                Id = RequireId(xmlFlowNode),
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                OutputMappings = outputMappings,
                FlowzerFormKey = ReadFormKey(xmlFlowNode.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "formDefinition"))
            };
        }


        return returnEvent;
    }

    private static TimerEventDefinition ParseTimerEventDefinition(XElement xElementTimerEventDefinition)
    {
        TimerEventDefinition? timerEventDefinition = null;
        var element = xElementTimerEventDefinition.Descendants().FirstOrDefault(x => x.Name.LocalName == "timeCycle");
        if (element != null)
        {
            timerEventDefinition = new TimerEventDefinition()
            {
                TimeCycle = new BPMN.Common.Expression
                {
                    Body = element.Value,
                }
            };
        }

        element = xElementTimerEventDefinition.Descendants().FirstOrDefault(x => x.Name.LocalName == "timeDate");
        if (element != null)
        {
            timerEventDefinition = new TimerEventDefinition()
            {
                TimeDate = new BPMN.Common.Expression
                {
                    Body = element.Value,
                }
            };
        }

        element = xElementTimerEventDefinition.Descendants().FirstOrDefault(x => x.Name.LocalName == "timeDuration");
        if (element != null)
        {
            timerEventDefinition = new TimerEventDefinition()
            {
                TimeDuration = new BPMN.Common.Expression
                {
                    Body = element.Value,
                }
            };
        }

        // Ein Zeitereignis ohne timeCycle, timeDate oder timeDuration hat keinen Termin. Das ist
        // ein benannter Modellfehler — die Veroeffentlichungspruefung meldet ihn als
        // bpmn.timer.definition_required am Knoten.
        return timerEventDefinition ?? throw new ModelValidationException(
            $"The timer event '{xElementTimerEventDefinition.Parent?.Attribute("id")?.Value ?? "(unknown)"}' "
            + "requires timeCycle, timeDate, or timeDuration.");
    }
}
