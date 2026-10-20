using System.Xml.Linq;
using FluentAssertions;
using WebApiEngine.ProcessPackages;

namespace WebApiEngine.Tests;

public sealed class DynamicDirectoryProcessPackageTest
{
    // Testzweck: Quellen enthalten keine installationsgebundene Benutzer-ID. Export und
    // Importmapping müssen sie semantisch erhalten, ohne einen Namens-/ID-Ersatz zu erzeugen.
    [TestCase("initiator")]
    [TestCase("variable:vertretung")]
    public void PackageReferences_ShouldPreserveSourcesWithoutIdentityMappings(string source)
    {
        var xml = $"<bpmn:definitions xmlns:bpmn='http://www.omg.org/spec/BPMN/20100524/MODEL' xmlns:flowzer='https://flowzer.io/schema/bpmn/1.0'><bpmn:process id='Process'><bpmn:userTask id='Task'><bpmn:extensionElements><flowzer:taskAssignment mode='directory' assigneeSource='{source}' /></bpmn:extensionElements></bpmn:userTask></bpmn:process></bpmn:definitions>";
        var scan = ProcessPackageReferences.Extract(xml, ProcessPackageDirectoryNames.Empty);
        scan.References.Should().BeEmpty();
        var applied = ProcessPackageReferences.Apply(scan.Xml, new Dictionary<string, string>());
        var assignment = XDocument.Parse(applied).Descendants(XName.Get("taskAssignment", "https://flowzer.io/schema/bpmn/1.0")).Single();
        assignment.Attribute("assigneeSource")!.Value.Should().Be(source);
        assignment.Attribute("assigneeId").Should().BeNull();
    }
}
