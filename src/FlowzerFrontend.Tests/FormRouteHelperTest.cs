using FluentAssertions;
using FlowzerFrontend.BusinessLogic;

namespace FlowzerFrontend.Tests;

public class FormRouteHelperTest
{
    // Testzweck: Deckt den Fall „Parse Should Recognize Create Route“ ab.
    [Test]
    public void Parse_ShouldRecognizeCreateRoute()
    {
        var result = FormRouteHelper.Parse("create");

        result.IsCreate.Should().BeTrue();
        result.FormId.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    // Testzweck: Deckt den Fall „Parse Should Return GUID When Route Contains Existing Form ID“ ab.
    [Test]
    public void Parse_ShouldReturnGuid_WhenRouteContainsExistingFormId()
    {
        var formId = Guid.NewGuid();

        var result = FormRouteHelper.Parse(formId.ToString());

        result.IsCreate.Should().BeFalse();
        result.FormId.Should().Be(formId);
        result.ErrorMessage.Should().BeNull();
    }

    // Testzweck: Deckt den Fall „Parse Should Return Error For Invalid Route Value“ ab.
    [Test]
    public void Parse_ShouldReturnError_ForInvalidRouteValue()
    {
        var result = FormRouteHelper.Parse("not-a-guid");

        result.IsCreate.Should().BeFalse();
        result.FormId.Should().BeNull();
        result.ErrorMessage.Should().Contain("not-a-guid");
    }
}
