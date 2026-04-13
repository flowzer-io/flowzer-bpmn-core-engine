using FluentAssertions;

namespace WebApiEngine.Tests;

public class ModelVersionTest
{
    // Testzweck: Deckt den Fall „Equality Operators Should Compare Major And Minor Values“ ab.
    [Test]
    public void EqualityOperators_ShouldCompareMajorAndMinorValues()
    {
        var left = new Model.Version(1, 2);
        var equalRight = new Model.Version(1, 2);
        var differentRight = new Model.Version(1, 3);

        (left == equalRight).Should().BeTrue();
        (left != equalRight).Should().BeFalse();
        (left == differentRight).Should().BeFalse();
        left.Equals(equalRight).Should().BeTrue();
        left.GetHashCode().Should().Be(equalRight.GetHashCode());
    }

    // Testzweck: Deckt den Fall „Plus Operator Should Return New Instance Without Mutating Original Version“ ab.
    [Test]
    public void PlusOperator_ShouldReturnNewInstance_WithoutMutatingOriginalVersion()
    {
        var original = new Model.Version(2, 4);

        var incremented = original + 1;

        original.ToString().Should().Be("2.4");
        incremented.ToString().Should().Be("2.5");
        incremented.Should().NotBeSameAs(original);
    }
}
