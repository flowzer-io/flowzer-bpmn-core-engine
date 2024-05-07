using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BPMN.Activities;
using BPMN.Common;
using BPMN.Events;
using BPMN.Flowzer;
using BPMN.Gateways;
using BPMN.HumanInteraction;
using BPMN.Infrastructure;
using BPMN.Process;
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
    public static Definitions ParseModel(string xml)
    {
        var xDocument = XDocument.Parse(xml);
        var root = xDocument.Root!;
        var model = new Definitions
        {
            Id = root.Attribute("id")!.Value,
            FlowzerFileHash = GetHash(xml),
        };

        // Gib mir alle Nodes unter root, die vom Typ "bpmn:process" sind

        var xmlProcessNodes = root.Elements().Where(n =>
            n.Name.LocalName.Equals("process", StringComparison.InvariantCultureIgnoreCase) &&
            n.Attribute("isExecutable")?.Value.Equals("true", StringComparison.InvariantCultureIgnoreCase) == true);

        foreach (var xmlProcessNode in xmlProcessNodes)
        {
            var process = new Process
            {
                Id = xmlProcessNode.Attribute("id")!.Value,
                Name = xmlProcessNode.Attribute("name")?.Value,
                IsExecutable = true,
                FlowzerProcessHash = GetHash(xmlProcessNode.ToString())
            };
            model.RootElements.Add(process);

            foreach (var xmlFlowNode in xmlProcessNode.Elements())
            {
                var inputMappings = 
                    xmlFlowNode.Descendants().Where(x => x.Name.LocalName == "input")
                        .Select(x => new FlowzerIoMapping(
                        x.Attribute("source")!.Value,
                        x.Attribute("target")!.Value)).ToList();
                if (inputMappings.Count == 0) inputMappings = null;

                var outputMappings = 
                    xmlFlowNode.Descendants().Where(x => x.Name.LocalName == "output")
                    .Select(x => new FlowzerIoMapping(
                        x.Attribute("source")!.Value,
                        x.Attribute("target")!.Value)).ToList();
                if (outputMappings.Count == 0) outputMappings = null;

                switch (xmlFlowNode.Name.LocalName)
                {
                    case "startEvent":
                        HandleStartEvent(process, xmlFlowNode);
                        break;

                    case "serviceTask":
                        process.FlowElements.Add(new ServiceTask
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            Container = process,
                            DefaultId = xmlFlowNode.Attribute("name")?.Value,
                            Implementation =
                                xmlFlowNode.Attribute("Implementation")?.Value
                                ?? xmlFlowNode.Descendants()
                                    .FirstOrDefault(e => e.Name.LocalName == "taskDefinition")
                                    ?.Attribute("type")?.Value
                                ?? throw new Exception("No implementation found"),
                            InputMappings = inputMappings,
                            OutputMappings = outputMappings,
                        });
                        break;

                    case "userTask":
                        var formDefinition = xmlFlowNode.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "formDefinition");
                        var assignmentDefinition = xmlFlowNode.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "assignmentDefinition");
                        var taskSchedule = xmlFlowNode.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "taskSchedule");
                        process.FlowElements.Add(new UserTask
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            Container = process,
                            DefaultId = xmlFlowNode.Attribute("name")?.Value,
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
                        });
                        break;

                    case "task":
                        process.FlowElements.Add(new Task
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            Container = process,
                            DefaultId = xmlFlowNode.Attribute("name")?.Value,
                        });
                        break;

                    case "exclusiveGateway":
                        process.FlowElements.Add(new ExclusiveGateway
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            Container = process,
                            DefaultId = xmlFlowNode.Attribute("name")?.Value,
                        });
                        break;

                    case "complexGateway":
                        process.FlowElements.Add(new ComplexGateway()
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            Container = process,
                            DefaultId = xmlFlowNode.Attribute("name")?.Value,
                        });
                        break;

                    case "inclusiveGateway":
                        process.FlowElements.Add(new InclusiveGateway()
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            Container = process,
                            DefaultId = xmlFlowNode.Attribute("name")?.Value,
                        });
                        break;

                    case "parallelGateway":
                        process.FlowElements.Add(new ParallelGateway
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            Container = process,
                        });
                        break;

                    case "endEvent":
                        process.FlowElements.Add(new EndEvent
                        {
                            Id = xmlFlowNode.Attribute("id")!.Value,
                            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                            Container = process,
                        });
                        break;

                    case "extensionElements":
                    case "sequenceFlow":
                        break;

                    default:
                        throw new NotSupportedException($"{xmlFlowNode.Name} is not supported at moment.");
                }
            }

            foreach (var xmlFlowNode in xmlProcessNode.Elements().Where(x => x.Name.LocalName == "sequenceFlow"))
            {
                var sourceRef = xmlFlowNode.Attribute("sourceRef")!.Value;
                var targetRef = xmlFlowNode.Attribute("targetRef")!.Value;
                var source = (FlowNode)process.FlowElements.Single(e => e.Id == sourceRef);
                var target = (FlowNode)process.FlowElements.Single(e => e.Id == targetRef);
                var newSequenceFlow = new SequenceFlow
                {
                    Id = xmlFlowNode.Attribute("id")!.Value,
                    Name = xmlFlowNode.Attribute("name")?.Value ?? "",
                    SourceRef = source,
                    TargetRef = target,
                    Container = process,
                    FlowzerCondition = xmlFlowNode.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "conditionExpression")
                        ?.Value,
                };
                if (source.GetType().GetInterfaces().Contains(typeof(IHasDefault))
                    && ((IHasDefault)source).DefaultId == newSequenceFlow.Id)
                {
                    newSequenceFlow = newSequenceFlow with { FlowzerIsDefault = true };
                    ((IHasDefault)source).Default = newSequenceFlow;
                    Console.WriteLine("Implementiert Default");
                }

                process.FlowElements.Add(newSequenceFlow);
            }
        }

        return model;
    }

    private static void HandleStartEvent(Process process, XElement xmlFlowNode)
    {
        // if (xmlFlowNode.Descendants().Any(x => x.Name.LocalName == "messageEventDefinition"))
        // {
        //     process.FlowElements.Add(new MessageStartEvent
        //     {
        //         Id = xmlFlowNode.Attribute("id")!.Value,
        //         Name = xmlFlowNode.Attribute("name")?.Value ?? "",
        //         Container = process,
        //     });
        //     return;
        // }
        
        process.FlowElements.Add(new StartEvent
        {
            Id = xmlFlowNode.Attribute("id")!.Value,
            Name = xmlFlowNode.Attribute("name")?.Value ?? "",
            Container = process,
        });
    }

    /// <summary>
    /// Gibt den Hash eines Strings zurück
    /// </summary>
    /// <param name="value">Den zu verarbeitenden Wert</param>
    /// <returns>Den MD5 Hash als String</returns>
    private static string GetHash(string value)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = md5.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}