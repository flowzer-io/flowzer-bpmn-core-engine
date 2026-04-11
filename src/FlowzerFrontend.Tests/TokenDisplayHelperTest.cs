using System.Dynamic;
using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class TokenDisplayHelperTest
{
    [Test]
    public void GetImplementation_ShouldReturnNull_WhenCurrentFlowElementIsMissing()
    {
        var token = new TokenDto();

        var result = TokenDisplayHelper.GetImplementation(token);

        result.Should().BeNull();
    }

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
