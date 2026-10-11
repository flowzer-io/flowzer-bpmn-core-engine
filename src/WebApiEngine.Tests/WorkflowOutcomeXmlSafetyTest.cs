using System.Xml.Linq;
using BPMN.Events;
using BPMN.Flowzer.Events;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Model;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Der reviewte Hash ist notwendig, aber ersetzt weder reguläre Endtyp-/Scopeprüfung noch Versionsidentität.</summary>
[NonParallelizable]
public sealed class WorkflowOutcomeXmlSafetyTest
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    // Testzweck: Auch neu reviewter XML-Hash erlaubt nur die zwei regulären direkten None-Rootenden, niemals Event-/Nested-Enden.
    [TestCase("terminateEventDefinition")]
    [TestCase("errorEventDefinition")]
    [TestCase("escalationEventDefinition")]
    [TestCase("messageEventDefinition")]
    [TestCase("signalEventDefinition")]
    [TestCase("timerEventDefinition")]
    [TestCase("unknownEventDefinition")]
    [TestCase("duplicate-id")]
    [TestCase("nested-approved-end")]
    [TestCase("extra-root-end")]
    [TestCase("outgoing-end")]
    [TestCase("wrong-xml-process")]
    [TestCase("wrong-xml-definitions")]
    [TestCase("malformed-xml")]
    [TestCase("dtd")]
    public async Task UnsupportedReviewedXml_ShouldRemainUnknown(string scenario)
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var xml = await context.Demo.Storage.DefinitionStorage.GetBinary(context.Demo.DefinitionId);
        var document = XDocument.Parse(xml);
        var process = document.Root!.Elements(Bpmn + "process").Single();
        var end = process.Elements(Bpmn + "endEvent").Single(element => (string?)element.Attribute("id") == "End_Approved");
        switch (scenario)
        {
            case "duplicate-id": process.Add(new XElement(Bpmn + "endEvent", new XAttribute("id", "End_Approved"))); break;
            case "nested-approved-end": end.Remove(); process.Elements(Bpmn + "subProcess").Single().Add(end); break;
            case "extra-root-end": process.Add(new XElement(Bpmn + "endEvent", new XAttribute("id", "Extra"))); break;
            case "outgoing-end": process.Add(new XElement(Bpmn + "sequenceFlow", new XAttribute("id", "Fake"), new XAttribute("sourceRef", "End_Approved"), new XAttribute("targetRef", "End_Rejected"))); break;
            case "wrong-xml-process": process.SetAttributeValue("id", "other"); break;
            case "wrong-xml-definitions": document.Root.SetAttributeValue("id", "other"); break;
            default: if (scenario.EndsWith("EventDefinition", StringComparison.Ordinal)) end.Add(new XElement(Bpmn + scenario)); break;
        }
        xml = scenario switch
        {
            "malformed-xml" => "<broken",
            "dtd" => "<!DOCTYPE definitions [<!ENTITY forbidden 'none'>]>" + document,
            _ => document.ToString()
        };
        await context.Demo.Storage.DefinitionStorage.StoreBinary(context.Demo.DefinitionId, xml);
        context.Mapping.BpmnSha256 = WorkflowOutcomeTestContext.Hash(xml);
        (await context.Projector.ProjectAsync(await context.Demo.InstanceAsync())).Should().Be(ProcessInstanceOutcomeDto.Unknown);
    }

    // Testzweck: Tatsächlich geladene Definition muss zur Katalogkennung gehören; die GUID allein genügt nicht.
    [Test]
    public async Task WrongStoredDefinitionCatalog_ShouldRemainUnknown()
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var definition = await context.Demo.Storage.DefinitionStorage.GetDefinitionById(context.Demo.DefinitionId);
        definition.DefinitionId = "other";
        await context.Demo.Storage.DefinitionStorage.StoreDefinition(definition);
        (await context.Projector.ProjectAsync(await context.Demo.InstanceAsync())).Should().Be(ProcessInstanceOutcomeDto.Unknown);
    }

    // Testzweck: Gleichnamiges Roottoken mit einem Nicht-None-Endtyp darf trotz passender IDs nie Approved liefern.
    [Test]
    public async Task StoredTerminateEndToken_ShouldRemainUnknown()
    {
        using var context = await WorkflowOutcomeTestContext.CreateAsync(approve: true);
        var instance = await context.Demo.InstanceAsync();
        var end = instance.Tokens.Single(token => token.CurrentBaseElement.Id == "End_Approved");
        instance.Tokens.Remove(end);
        instance.Tokens.Add(WorkflowOutcomeProjectionSafetyTest.Copy(end, new FlowzerTerminateEvent { Id = "End_Approved", Name = "Synthetic" }));
        (await context.Projector.ProjectAsync(instance)).Should().Be(ProcessInstanceOutcomeDto.Unknown);
    }

    // Testzweck: Die tatsächlichen Optionsnamen binden über IConfiguration, inklusive verpflichtendem XML-Hash; ungültige GUIDs bleiben als Daten prüfbar.
    [Test]
    public void ConfigurationBinding_ShouldPreserveExactContractNames()
    {
        var values = new Dictionary<string, string?>
        {
            ["WorkflowOutcomes:Definitions:0:DefinitionId"] = "invalid-guid",
            ["WorkflowOutcomes:Definitions:0:CatalogId"] = "catalog",
            ["WorkflowOutcomes:Definitions:0:ProcessId"] = "process",
            ["WorkflowOutcomes:Definitions:0:ApprovedRootEndId"] = "approved",
            ["WorkflowOutcomes:Definitions:0:RejectedRootEndId"] = "rejected",
            ["WorkflowOutcomes:Definitions:0:BpmnSha256"] = new string('A', 64)
        };
        var options = new ConfigurationBuilder().AddInMemoryCollection(values).Build()
            .GetSection(WorkflowOutcomeOptions.SectionName).Get<WorkflowOutcomeOptions>()!;
        options.Definitions.Should().ContainSingle();
        options.Definitions[0].DefinitionId.Should().Be("invalid-guid");
        options.Definitions[0].BpmnSha256.Should().Be(new string('A', 64));
        options.Definitions[0].ApprovedRootEndId.Should().Be("approved");
        options.Definitions[0].RejectedRootEndId.Should().Be("rejected");
    }
}
