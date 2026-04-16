using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class FormListViewHelperTest
{
    // Testzweck: Deckt den Fall „Apply Query Should Filter Across Name And Form Identifier“ ab.
    [Test]
    public void ApplyQuery_ShouldFilterAcrossNameAndFormIdentifier()
    {
        var firstFormId = Guid.NewGuid();
        var forms = new[]
        {
            new FormMetaDataDto
            {
                FormId = firstFormId,
                Name = "Invoice approval"
            },
            new FormMetaDataDto
            {
                FormId = Guid.NewGuid(),
                Name = "Vacation request"
            }
        };

        var result = FormListViewHelper.ApplyQuery(forms, firstFormId.ToString()[..8]).ToArray();

        result.Should().ContainSingle();
        result[0].FormId.Should().Be(firstFormId);
    }

    // Testzweck: Deckt den Fall „Apply Query Should Sort Forms Alphabetically By Name“ ab.
    [Test]
    public void ApplyQuery_ShouldSortFormsAlphabeticallyByName()
    {
        var forms = new[]
        {
            new FormMetaDataDto
            {
                FormId = Guid.NewGuid(),
                Name = "Zeta form"
            },
            new FormMetaDataDto
            {
                FormId = Guid.NewGuid(),
                Name = "Alpha form"
            }
        };

        var result = FormListViewHelper.ApplyQuery(forms, null).ToArray();

        result.Select(form => form.Name).Should().Equal("Alpha form", "Zeta form");
    }
}
