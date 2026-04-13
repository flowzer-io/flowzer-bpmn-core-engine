using System.Dynamic;
using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class TokenDisplayHelperTest
{
    // Testzweck: Deckt den Fall „Get Implementation Should Return Null When Current Flow Element Is Missing“ ab.
    [Test]
    public void GetImplementation_ShouldReturnNull_WhenCurrentFlowElementIsMissing()
    {
        var token = new TokenDto();

        var result = TokenDisplayHelper.GetImplementation(token);

        result.Should().BeNull();
    }

    // Testzweck: Deckt den Fall „Get Flow Element ID Should Fallback To Current Flow Node ID When Expando Value Is Missing“ ab.
    [Test]
    public void GetFlowElementId_ShouldFallbackToCurrentFlowNodeId_WhenExpandoValueIsMissing()
    {
        var token = new TokenDto
        {
            CurrentFlowNodeId = "Activity_UserTask"
        };

        var result = TokenDisplayHelper.GetFlowElementId(token);

        result.Should().Be("Activity_UserTask");
    }

    // Testzweck: Deckt den Fall „Get Implementation Should Return Null When Expando Contains Null Value“ ab.
    [Test]
    public void GetImplementation_ShouldReturnNull_WhenExpandoContainsNullValue()
    {
        dynamic flowElement = new ExpandoObject();
        flowElement.Implementation = null;

        var token = new TokenDto
        {
            CurrentFlowElement = flowElement
        };

        var result = TokenDisplayHelper.GetImplementation(token);

        result.Should().BeNull();
    }

    // Testzweck: Deckt den Fall „Get Display Name Should Prefer Name And Fallback To ID“ ab.
    [Test]
    public void GetDisplayName_ShouldPreferName_AndFallbackToId()
    {
        dynamic flowElement = new ExpandoObject();
        flowElement.Name = "Approve invoice";
        flowElement.Id = "Activity_UserTask";

        var token = new TokenDto
        {
            CurrentFlowElement = flowElement
        };

        var result = TokenDisplayHelper.GetDisplayName(token, "(fallback)");

        result.Should().Be("Approve invoice");
    }
}
