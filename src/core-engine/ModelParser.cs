using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using BPMN.Foundation;
using Task = BPMN.Activities.Task;

namespace core_engine;

public static class ModelParser
{
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
    private static Definitions ParseModel(string xml)
    {
        var xDocument = XDocument.Parse(xml);
        var root = xDocument.Root!;
        var definitionsId = root.Attribute("id")!.Value;

        FlowzerList<IRootElement> rootElements = [];

        rootElements.AddRange(ParseMessages(root));
        rootElements.AddRange(ParseSignals(root));
        rootElements.AddRange(ParseProcess(root, rootElements, definitionsId));

        var definitions = new Definitions
        {
            Id = definitionsId,
            RootElements = rootElements
        };

        return definitions;
    }

    private static IEnumerable<MessageDefinition> ParseMessages(XElement root)
    {
        return root.Elements().Where(n =>
                n.Name.LocalName.Equals("message", StringComparison.InvariantCultureIgnoreCase))
            .Select(m => new MessageDefinition
            {
                Name = m.Attribute("name")!.Value,
                FlowzerId = m.Attribute("id")?.Value,
                FlowzerCorrelationKey = m.Descendants()
                    .SingleOrDefault(s => s.Name.LocalName == "subscription")?
                    .Attribute("correlationKey")?.Value,
            });
    }

    private static IEnumerable<Signal> ParseSignals(XElement root)
    {
        return root.Elements().Where(n =>
                n.Name.LocalName.Equals("signal", StringComparison.InvariantCultureIgnoreCase))
            .Select(m => new Signal()
            {
                Name = m.Attribute("name")!.Value,
                FlowzerId = m.Attribute("id")?.Value,
            });
    }

    private static List<Process> ParseProcess(XElement root, List<IRootElement> rootElements,
        string definitionsId)
    {
        // Gib mir alle Nodes unter root, die vom Typ "bpmn:process" sind

        FlowzerList<Process> processes = [];

        var xmlProcessNodes = root.Elements().Where(n =>
            n.Name.LocalName.Equals("process", StringComparison.InvariantCultureIgnoreCase) &&
            (bool)n.Attribute("isExecutable")?.Value.Equals("true", StringComparison.InvariantCultureIgnoreCase));

        foreach (var xmlProcessNode in xmlProcessNodes)
        {
            FlowzerList<FlowElement> flowElements = [];

            foreach (var xmlFlowNode in xmlProcessNode.Elements())
            {
                var inputMappings =
                    xmlFlowNode.Descendants().Where(x => x.Name.LocalName == "input")
                        .Select(x =>
                            new FlowzerIoMapping(
                                x.Attribute("source")!.Value,
                                x.Attribute("target")!.Value))
                        .ToFlowzerList();
                if (!inputMappings.Any()) inputMappings = null;

                var outputMappings =
                    xmlFlowNode.Descendants().Where(x => x.Name.LocalName == "output")
                        .Select(x =>
                            new FlowzerIoMapping(
                                x.Attribute("source")!.Value,
                                x.Attribute("target")!.Value))
                        .ToFlowzerList();
                if (!outputMappings.Any()) outputMappings = null;

                switch (xmlFlowNode.Name.LocalName)
                {
                    case "startEvent":
                        flowElements.Add(HandleStartEvent(xmlFlowNode, rootElements, outputMappings));
                        break;

                    case "intermediateCatchEvent":
                    case "intermediateThrowEvent":
                        flowElements.Add(HandleIntermediateEvent(xmlFlowNode, rootElements,
                            xmlFlowNode.Name.LocalName));
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
                    
                    case "receiveTask":
                        flowElements.Add(new ReceiveTask
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            DefaultId = xmlFlowNode.Attribute("name")?.Value,
                            MessageRef = rootElements.OfType<MessageDefinition>()
                                .Single(m => m.FlowzerId == xmlFlowNode.Attribute("messageRef")?.Value),
                        });
                        break;


                    case "task":
                        flowElements.Add(new Task
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            DefaultId = xmlFlowNode.Attribute("name")?.Value,
                        });
                        break;
                    
                    case "manualTask":
                        flowElements.Add(new ManualTask
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        });
                        break;

                    case "exclusiveGateway":
                        flowElements.Add(new ExclusiveGateway
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        });
                        break;

                    case "complexGateway":
                        flowElements.Add(new ComplexGateway()
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        });
                        break;

                    case "inclusiveGateway":
                        flowElements.Add(new InclusiveGateway()
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            DefaultId = xmlFlowNode.Attribute("default")?.Value,
                        });
                        break;

                    case "parallelGateway":
                        flowElements.Add(new ParallelGateway
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        });
                        break;

                    case "endEvent":
                        flowElements.Add(HandleEndEvent(xmlFlowNode, rootElements));
                        break;

                    case "extensionElements":
                    case "sequenceFlow":
                    case "boundaryEvent":
                        break;

                    default:
                        throw new NotSupportedException($"{xmlFlowNode.Name} is not supported at moment.");
                }
            }

            foreach (var xmlFlowNode in xmlProcessNode.Elements().Where(x => x.Name.LocalName == "sequenceFlow"))
            {
                var sourceRef = xmlFlowNode.Attribute("sourceRef")!.Value;
                var targetRef = xmlFlowNode.Attribute("targetRef")!.Value;
                var source = (FlowNode)flowElements.Single(e => e.Id == sourceRef);
                var target = (FlowNode)flowElements.Single(e => e.Id == targetRef);
                var isDefault = source.GetType().GetInterfaces().Contains(typeof(IHasDefault))
                                && ((IHasDefault)source).DefaultId == xmlFlowNode.Attribute("id")!.Value;
                var newSequenceFlow = new SequenceFlow
                {
                    Id = xmlFlowNode.Attribute("id")!.Value,
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

            foreach (var xmlFlowNode in xmlProcessNode.Elements().Where(x => x.Name.LocalName == "boundaryEvent"))
            {
                var attachedToRef = xmlFlowNode.Attribute("attachedToRef")!.Value;
                var attachedTo = (Activity)flowElements.Single(e => e.Id == attachedToRef);
                if (!bool.TryParse(xmlFlowNode.Attribute("cancelActivity")?.Value, out var cancelActivity))
                    cancelActivity = true;
                if (xmlFlowNode.HasDescendant("messageEventDefinition", out var definition))
                {
                    flowElements.Add(new FlowzerBoundaryMessageEvent
                    {
                        Id = xmlFlowNode.Attribute("id")!.Value,
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        MessageDefinition = rootElements.OfType<MessageDefinition>()
                            .Single(m => m.FlowzerId == definition.Attribute("messageRef")?.Value),
                        AttachedToRef = attachedTo,
                        CancelActivity = cancelActivity
                    });
                    continue;
                }
                
                if (xmlFlowNode.HasDescendant("signalEventDefinition", out definition))
                {
                    flowElements.Add(new FlowzerBoundarySignalEvent()
                    {
                        Id = xmlFlowNode.Attribute("id")!.Value,
                        Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                        Signal = rootElements.OfType<Signal>()
                            .Single(m => m.FlowzerId == definition.Attribute("signalRef")?.Value),
                        AttachedToRef = attachedTo,
                        CancelActivity = cancelActivity
                    });
                    continue;
                }
                throw new NotSupportedException($"{xmlFlowNode.Name} is not supported at moment.");
            }

            var process = new Process
            {
                Id = xmlProcessNode.Attribute("id")!.Value,
                Name = xmlProcessNode.Attribute("name")?.Value,
                IsExecutable = true,
                FlowElements = flowElements,
                DefinitionsId = definitionsId,
            };
            processes.Add(process);
        }

        return processes;
    }

    private static EndEvent HandleEndEvent(XElement xmlFlowNode, List<IRootElement> rootElements)
    {
        if (xmlFlowNode.HasDescendant("terminateEventDefinition", out _))
        {
            return new FlowzerTerminateEvent
            {
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            };
        }
        
        if (xmlFlowNode.HasDescendant("messageEventDefinition", out var definition))
        {
            xmlFlowNode.HasDescendant("taskDefinition", out var taskDefinition);
            return new FlowzerMessageEndEvent
            {
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                Implementation = taskDefinition?.Attribute("type")?.Value ?? "",
            };
        }
        
        if (xmlFlowNode.HasDescendant("signalEventDefinition", out definition))
        {
            return new FlowzerSignalEndEvent()
            {
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                Signal = rootElements.OfType<Signal>()
                    .Single(m => m.FlowzerId == definition.Attribute("signalRef")?.Value),
            };
        }
        
        return new EndEvent
        {
            Id = xmlFlowNode.Attribute("id")!.Value,
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
        };
    }

    private static Event HandleIntermediateEvent(XElement xmlFlowNode, List<IRootElement> rootElements,
        string name)
    {
        if (xmlFlowNode.HasDescendant("timerEventDefinition", out var definition))
        {
            return new FlowzerIntermediateTimerCatchEvent()
            {
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                TimerDefinition = ParseTimerEventDefinition(definition),
            };
        }
        
        var catchEvent = name.Contains("catch", StringComparison.InvariantCultureIgnoreCase);
        if (xmlFlowNode.HasDescendant("messageEventDefinition", out definition))
        {
            xmlFlowNode.HasDescendant("taskDefinition", out var taskDefinition);
            return catchEvent
                ? new FlowzerIntermediateMessageCatchEvent()
                {
                    Id = xmlFlowNode.Attribute("id")!.Value,
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    MessageDefinition = rootElements.OfType<MessageDefinition>()
                        .Single(m => m.FlowzerId == definition.Attribute("messageRef")?.Value),
                }
                : new FlowzerIntermediateMessageThrowEvent()
                {
                    Id = xmlFlowNode.Attribute("id")!.Value,
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    Implementation = taskDefinition?.Attribute("type")?.Value ?? "",
                };
        }

        if (xmlFlowNode.HasDescendant("signalEventDefinition", out definition))
        {
            return catchEvent
                ? new FlowzerIntermediateSignalCatchEvent()
                {
                    Id = xmlFlowNode.Attribute("id")!.Value,
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    Signal = rootElements.OfType<Signal>()
                        .Single(m => m.FlowzerId == definition.Attribute("signalRef")?.Value),
                }
                : new FlowzerIntermediateSignalThrowEvent()
                {
                    Id = xmlFlowNode.Attribute("id")!.Value,
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    Signal = rootElements.OfType<Signal>()
                        .Single(m => m.FlowzerId == definition.Attribute("signalRef")?.Value),
                };
        }

        if (catchEvent)
            return new IntermediateCatchEvent()
            {
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            };
        return new IntermediateThrowEvent()
        {
            Id = xmlFlowNode.Attribute("id")!.Value,
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
        };
    }

    private static ServiceTask HandleServiceTask(XElement xmlFlowNode, FlowzerList<FlowzerIoMapping>? inputMappings,
        FlowzerList<FlowzerIoMapping>? outputMappings)
    {
        return new ServiceTask
        {
            Id = xmlFlowNode.Attribute("id")!.Value,
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            // Container = process,
            DefaultId = xmlFlowNode.Attribute("default")?.Value,
            Implementation =
                xmlFlowNode.Attribute("Implementation")?.Value
                ?? xmlFlowNode.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "taskDefinition")
                    ?.Attribute("type")?.Value
                ?? throw new ModelValidationException(
                    $"Implementation not defined for Service task '{xmlFlowNode.Attribute("id")!.Value}'"),
            InputMappings = inputMappings,
            OutputMappings = outputMappings,
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
            Id = xmlFlowNode.Attribute("id")!.Value,
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
        };
    }

    private static UserTask HandleUserTask(XElement xmlFlowNode, FlowzerList<FlowzerIoMapping>? inputMappings,
        FlowzerList<FlowzerIoMapping>? outputMappings)
    {
        var formDefinition = xmlFlowNode.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "formDefinition");
        var assignmentDefinition = xmlFlowNode.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "assignmentDefinition");
        var taskSchedule = xmlFlowNode.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "taskSchedule");
        return new UserTask
        {
            Id = xmlFlowNode.Attribute("id")!.Value,
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            // Container = process,
            DefaultId = xmlFlowNode.Attribute("default")?.Value,
            Implementation =
                formDefinition?.Attribute("formKey")?.Value
                ?? formDefinition?.Attribute("formId")?.Value
                ?? throw new Exception("No implementation found"),
            FlowzerAssignee = assignmentDefinition?.Attribute("assignee")?.Value,
            FlowzerCandidateGroups = assignmentDefinition?.Attribute("candidateGroups")?.Value,
            FlowzerCandidateUsers = assignmentDefinition?.Attribute("candidateUsers")?.Value,
            FlowzerDueDate = taskSchedule?.Attribute("dueDate")?.Value,
            FlowzerFollowUpDate = taskSchedule?.Attribute("followUpDate")?.Value,
            InputMappings = inputMappings,
            OutputMappings = outputMappings,
        };
    }

    private static bool HasDescendant(this XElement element, string name,
        [NotNullWhen(returnValue: true)] out XElement? descendant)
    {
        descendant = element.Descendants()
            .SingleOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.InvariantCultureIgnoreCase));
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
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                // Container = process,
                TimerDefinition = ParseTimerEventDefinition(definition),
                OutputMappings = outputMappings
            };
        }

        else if (xmlFlowNode.HasDescendant("messageEventDefinition", out definition))
        {
            returnEvent = new FlowzerMessageStartEvent
            {
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                // Container = process,
                MessageDefinition = rootElements.OfType<MessageDefinition>()
                    .Single(m => m.FlowzerId == definition.Attribute("messageRef")?.Value),
                OutputMappings = outputMappings
            };
        }

        else if (xmlFlowNode.HasDescendant("signalEventDefinition", out definition))
        {
            returnEvent = new FlowzerSignalStartEvent
            {
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                // Container = process,
                Signal = rootElements.OfType<Signal>()
                    .Single(m => m.FlowzerId == definition.Attribute("signalRef")?.Value),
                OutputMappings = outputMappings
            };
        }

        else
        {
            returnEvent = new StartEvent
            {
                Id = xmlFlowNode.Attribute("id")!.Value,
                Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                OutputMappings = outputMappings
            };
        }


        return returnEvent;
    }

    private static TimerEventDefinition ParseTimerEventDefinition(XElement xElementTimerEventDefinition)
    {
        TimerEventDefinition? timerEventDefinition = null;
        var element = xElementTimerEventDefinition.Descendants().SingleOrDefault(x => x.Name.LocalName == "timeCycle");
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

        element = xElementTimerEventDefinition.Descendants().SingleOrDefault(x => x.Name.LocalName == "timeDate");
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

        element = xElementTimerEventDefinition.Descendants().SingleOrDefault(x => x.Name.LocalName == "timeDuration");
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

        if (timerEventDefinition == null)
            throw new ArgumentException("TimerEventDefinition found, but no Implementation.");
        return timerEventDefinition;
    }
}