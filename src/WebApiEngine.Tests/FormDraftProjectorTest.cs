using System.Dynamic;
using System.Text.Json;
using FluentAssertions;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

public sealed class FormDraftProjectorTest
{
    // Testzweck: Ein unvollstaendiger Draft darf sichere Zeilenwerte behalten, aber
    // weder unbekannte Zeilenfelder noch falsche Objektformen unter einer deklarierten
    // Wiederholgruppe persistieren.
    [Test]
    public void RepeatGroup_ShouldProjectOnlyDeclaredSafeDraftRows()
    {
        var contract = FormContractCompiler.Compile("""
            {"flowzer":{"contractVersion":3},"components":[
              {"type":"datagrid","key":"rows","flowzer":{"repeat":{"maxItems":2}},
               "components":[{"type":"textfield","key":"name","validate":{"required":true}},
                             {"type":"number","key":"amount"}]}
            ]}
            """);

        FormDraftProjector.Project(contract, Data("{\"rows\":[{\"name\":\"\",\"amount\":2}]}"))
            .Should().Be("{\"rows\":[{\"amount\":2,\"name\":\"\"}]}");
        Action unknown = () => FormDraftProjector.Project(contract,
            Data("{\"rows\":[{\"name\":\"ok\",\"secret\":\"NICHT_SPIEGELN\"}]}"));
        var error = unknown.Should().Throw<FormSubmissionException>().Which;
        error.Errors["rows[0].secret"].Should().Contain("field.undeclared");
        JsonSerializer.Serialize(error.Errors).Should().NotContain("NICHT_SPIEGELN");
    }

    private static ExpandoObject Data(string json) => JsonSerializer.Deserialize<ExpandoObject>(json)!;
}
