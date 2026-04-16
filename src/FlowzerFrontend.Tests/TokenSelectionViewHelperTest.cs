using System.Dynamic;
using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class TokenSelectionViewHelperTest
{
    // Testzweck: Prüft, dass Variablen- und Outputdaten für die Instanzansicht getrennt und formatiert serialisiert werden.
    [Test]
    public void Create_ShouldReturnPrettyPrintedVariablesAndOutput()
    {
        dynamic variables = new ExpandoObject();
        variables.customer = "Ada";

        dynamic output = new ExpandoObject();
        output.approved = true;

        var token = new TokenDto
        {
            CurrentFlowNodeId = "Activity_Review",
            Variables = variables,
            OutputData = output
        };

        var result = TokenSelectionViewHelper.Create(token);

        result.HasVariables.Should().BeTrue();
        result.HasOutputData.Should().BeTrue();
        result.VariablesJson.Should().Contain("customer").And.Contain("Ada");
        result.OutputJson.Should().Contain("approved").And.Contain("true");
    }

    // Testzweck: Prüft, dass eine leere Tokenauswahl keine kaputte Darstellung erzeugt, sondern einen neutralen Ausgangszustand liefert.
    [Test]
    public void Create_ShouldReturnEmptySelection_WhenTokenIsNull()
    {
        var result = TokenSelectionViewHelper.Create(null);

        result.Title.Should().Be("Select a token");
        result.HasVariables.Should().BeFalse();
        result.HasOutputData.Should().BeFalse();
        result.VariablesJson.Should().BeEmpty();
        result.OutputJson.Should().BeEmpty();
    }
}
