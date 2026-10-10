using System.Dynamic;
using System.Text;
using core_engine;
using FluentAssertions;
using Model;
using Task = System.Threading.Tasks.Task;

namespace core_engine_tests;

/// <summary>Geschlossene Eingangssichten bleiben auch bei verschachtelten Subprozess-Abschlüssen geschlossen.</summary>
public sealed class NestedSubProcessMappingTest
{
    // Testzweck: Ein alter oder gemappter innerer Subprozess darf beim impliziten
    // Abschluss niemals den vollständigen Root in die äußere Feldprojektion importieren.
    [TestCase(false)]
    [TestCase(true)]
    public async Task InnerCompletion_ShouldNotImportExcludedRootFields(bool mappedInner)
    {
        var instance = await StartAsync(mappedInner, exportInner: false);
        var outer = instance.Tokens.Single(token => token.CurrentBaseElement.Id == "Outer");
        var fields = (IDictionary<string, object?>)outer.Variables!;
        fields.Should().NotContainKey("privateRootField");
        fields["public"].Should().Be("outer-value");
        instance.GetActiveUserTasks().Should().ContainSingle().Which.CurrentBaseElement.Id.Should().Be("AfterInner");
    }

    // Testzweck: Der explizite innere Scope liest den nächstliegenden gemappten Parent,
    // nicht gleichnamige Root-Werte; ausgelassene Felder erhalten keinen Root-Fallback.
    [Test]
    public async Task InnerInput_ShouldReadOnlyTheProjectedOuterScope()
    {
        var instance = await StartAsync(mappedInner: true, exportInner: false);
        var inner = instance.Tokens.Single(token => token.CurrentBaseElement.Id == "Inner");
        var fields = (IDictionary<string, object?>)inner.Variables!;
        fields.Keys.Should().BeEquivalentTo("public");
        fields["public"].Should().Be("outer-value");
    }

    // Testzweck: Nur die deklarierte innere Ausgabe reist zum Parent, aus der lokalen
    // Projektion statt aus dem Root-Alias; Root und ausgeschlossene Felder bleiben unverändert.
    [Test]
    public async Task ExplicitInnerOutput_ShouldExportTheLocalResultToItsParent()
    {
        var instance = await StartAsync(mappedInner: true, exportInner: true);
        var outer = instance.Tokens.Single(token => token.CurrentBaseElement.Id == "Outer");
        ((IDictionary<string, object?>)outer.Variables!)["exported"].Should().Be("outer-value");
        ((IDictionary<string, object?>)outer.Variables!).Should().NotContainKey("privateRootField");
        ((IDictionary<string, object?>)instance.MasterToken.Variables!)["public"].Should().Be("root-value");
        ((IDictionary<string, object?>)instance.MasterToken.Variables!).Should().NotContainKey("exported");
    }

    private static async Task<InstanceEngine> StartAsync(bool mappedInner, bool exportInner)
    {
        var innerMapping = mappedInner ? $$"""
          <bpmn:extensionElements><zeebe:ioMapping>
            <zeebe:input source="=public" target="public" />
            {{(exportInner ? "<zeebe:output source=\"=public\" target=\"exported\" />" : "")}}
          </zeebe:ioMapping></bpmn:extensionElements>
          """ : "";
        var xml = $$"""
          <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
            xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" id="NestedMapping" targetNamespace="test">
            <bpmn:process id="NestedMappingProcess" isExecutable="true">
              <bpmn:startEvent id="Start" />
              <bpmn:subProcess id="Outer">
                <bpmn:extensionElements><zeebe:ioMapping>
                  <zeebe:input source='="outer-value"' target="public" />
                </zeebe:ioMapping></bpmn:extensionElements>
                <bpmn:startEvent id="OuterStart" />
                <bpmn:subProcess id="Inner">
                  {{innerMapping}}
                  <bpmn:startEvent id="InnerStart" /><bpmn:endEvent id="InnerEnd" />
                  <bpmn:sequenceFlow id="I" sourceRef="InnerStart" targetRef="InnerEnd" />
                </bpmn:subProcess>
                <bpmn:userTask id="AfterInner" />
                <bpmn:sequenceFlow id="O1" sourceRef="OuterStart" targetRef="Inner" />
                <bpmn:sequenceFlow id="O2" sourceRef="Inner" targetRef="AfterInner" />
              </bpmn:subProcess>
              <bpmn:sequenceFlow id="R" sourceRef="Start" targetRef="Outer" />
            </bpmn:process>
          </bpmn:definitions>
          """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var model = await ModelParser.ParseModel(stream);
        dynamic variables = new ExpandoObject(); variables.@public = "root-value"; variables.privateRootField = "synthetic-private-marker";
        return new ProcessEngine(model.GetProcesses().Single(), Helper.TestFlowzerConfig).StartProcess(variables);
    }
}
