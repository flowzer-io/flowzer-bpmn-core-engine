using System.Dynamic;
using BPMN.Flowzer;
using BPMN.HumanInteraction;
using FluentAssertions;
using Model;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

public class TaskFormContextTest
{
    // Testzweck: Explizite Input-Mappings verhindern eine nachträgliche Erweiterung um
    // Parentvariablen. Ohne Mapping überlagern lokale Werte ausschließlich den nächsten Scope.
    [TestCase(false)]
    [TestCase(true)]
    public void Context_ShouldRespectLocalValuesAndExplicitInputMappings(bool mapped)
    {
        var parent = new Token { ProcessInstanceId = Guid.NewGuid(), CurrentBaseElement = new BPMN.Process.Process { Id = "Process", DefinitionsId = "Definitions" }, ActiveBoundaryEvents = [], Variables = Data(("parent", "visible"), ("same", "parent")) };
        var task = new Token
        {
            ProcessInstanceId = parent.ProcessInstanceId, ActiveBoundaryEvents = [], ParentTokenId = parent.Id, Variables = Data(("same", "local")),
            CurrentBaseElement = new UserTask { Id = "Review", Name = "Review", Implementation = "", InputMappings = mapped ? [new FlowzerIoMapping("source", "same")] : [] }
        };
        var result = (IDictionary<string, object?>)TaskFormContext.Read([parent, task], task);
        result["same"].Should().Be("local");
        result.ContainsKey("parent").Should().Be(!mapped);
    }

    // Testzweck: Fehlende oder zyklische Parents sind kein Anlass, einen fremden Scope
    // zu raten oder unvollständig freizugeben.
    [TestCase(false)]
    [TestCase(true)]
    public void Context_ShouldFailClosedForInvalidParents(bool cyclic)
    {
        var id = Guid.NewGuid();
        var task = new Token { Id = id, ProcessInstanceId = Guid.NewGuid(), ActiveBoundaryEvents = [],
            CurrentBaseElement = new UserTask { Id = "Review", Name = "Review", Implementation = "" },
            ParentTokenId = cyclic ? id : Guid.NewGuid(), Variables = Data(("local", "private")) };
        ((IDictionary<string, object?>)TaskFormContext.Read([task], task)).Should().BeEmpty();
    }

    private static ExpandoObject Data(params (string Key, string Value)[] values)
    {
        ExpandoObject result = new();
        foreach (var (key, value) in values) ((IDictionary<string, object?>)result)[key] = value;
        return result;
    }
}
