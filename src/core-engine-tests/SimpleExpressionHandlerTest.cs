using System.Globalization;
using System.Dynamic;
using core_engine.Expression;
using FluentAssertions;
using Flowzer.Shared;

namespace core_engine_tests;

public class SimpleExpressionHandlerTest
{
    private readonly SimpleExpressionHandler _handler = new();

    [Test]
    public void GetValue_ShouldResolveNumericLiteral()
    {
        var result = _handler.GetValue(new ExpandoObject(), "=12345");

        result.Should().Be(12345);
    }

    [Test]
    public void GetValue_ShouldParseJsonObjectAndArrayPayloads()
    {
        var result = _handler.GetValue(
            new ExpandoObject(),
            "={\"Address\":{\"Firstname\":\"Lukas\"},\"Tags\":[{\"Index\":1},{\"Index\":2}]}")
            .Should().BeOfType<ExpandoObject>()
            .Subject;

        result.GetValue("Address.Firstname").Should().Be("Lukas");

        var tags = result.GetValue("Tags").Should().BeOfType<List<object?>>().Subject;
        tags.Should().HaveCount(2);
        tags[0].GetValue("Index").Should().Be(1L);
        tags[1].GetValue("Index").Should().Be(2L);
    }

    [Test]
    public void MatchExpression_ShouldCompareNestedProperties()
    {
        var variables = new
        {
            Order = new
            {
                Address = new
                {
                    Firstname = "Lukas"
                }
            }
        }.ToExpando()!;

        var matches = _handler.MatchExpression(variables, "=Order.Address.Firstname = \"Lukas\"");

        matches.Should().BeTrue();
    }

    [Test]
    public void MatchExpression_ShouldEvaluateBooleanComparisons()
    {
        var variables = new
        {
            Person = new
            {
                HatZeit = true
            }
        }.ToExpando()!;

        var matches = _handler.MatchExpression(variables, "=Person.HatZeit = true");

        matches.Should().BeTrue();
    }

    [Test]
    public void GetValue_ShouldParseDecimalLiteralsCultureInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var germanCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentCulture = germanCulture;
            CultureInfo.CurrentUICulture = germanCulture;

            var result = _handler.GetValue(new ExpandoObject(), "=1.23");

            result.Should().Be(1.23d);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
