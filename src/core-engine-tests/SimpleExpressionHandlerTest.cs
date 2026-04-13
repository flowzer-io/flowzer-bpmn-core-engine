using System.Globalization;
using System.Dynamic;
using core_engine.Expression;
using FluentAssertions;
using Flowzer.Shared;

namespace core_engine_tests;

public class SimpleExpressionHandlerTest
{
    private readonly SimpleExpressionHandler _handler = new();

    // Testzweck: Deckt den Fall „Get Value Should Resolve Numeric Literal“ ab.
    [Test]
    public void GetValue_ShouldResolveNumericLiteral()
    {
        var result = _handler.GetValue(new ExpandoObject(), "=12345");

        result.Should().Be(12345);
    }

    // Testzweck: Deckt den Fall „Get Value Should Parse JSON Object And Array Payloads“ ab.
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

    // Testzweck: Deckt den Fall „Match Expression Should Compare Nested Properties“ ab.
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

    // Testzweck: Deckt den Fall „Match Expression Should Evaluate Boolean Comparisons“ ab.
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

    // Testzweck: Deckt den Fall „Get Value Should Parse Decimal Literals Culture Invariant“ ab.
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

    // Testzweck: Deckt den Fall „Create Default Should Fallback To Simple Expression Handler When Clear Script Is Unavailable“ ab.
    [Test]
    public void CreateDefault_ShouldFallbackToSimpleExpressionHandler_WhenClearScriptIsUnavailable()
    {
        var config = FlowzerConfig.CreateDefault(() => throw new DllNotFoundException("ClearScriptV8.linux-x64.so"));

        config.ExpressionHandler.Should().BeOfType<SimpleExpressionHandler>();
    }

    // Testzweck: Deckt den Fall „Create Default Should Keep Custom Expression Handler When Factory Succeeds“ ab.
    [Test]
    public void CreateDefault_ShouldKeepCustomExpressionHandler_WhenFactorySucceeds()
    {
        var expressionHandler = new SimpleExpressionHandler();

        var config = FlowzerConfig.CreateDefault(() => expressionHandler);

        config.ExpressionHandler.Should().BeSameAs(expressionHandler);
    }
}
