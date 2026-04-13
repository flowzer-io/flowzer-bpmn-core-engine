using System.Dynamic;
using FluentAssertions;
using FlowzerFrontend.BusinessLogic;

namespace FlowzerFrontend.Tests;

public class FrontendExpandoHelperTest
{
    // Testzweck: Prüft, dass ein Typkonflikt beim Lesen aus Expando-Objekten als InvalidCastException gemeldet wird.
    [Test]
    public void GetValue_ShouldThrowInvalidCastException_WhenStoredValueHasDifferentType()
    {
        dynamic values = new ExpandoObject();
        values.Count = "not-an-int";

        var action = () => ExpandoHelper.GetValue<int>((ExpandoObject)values, "Count", defaultValue: 0);

        action.Should().Throw<InvalidCastException>();
    }
}
