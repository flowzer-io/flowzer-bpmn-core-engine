using FluentAssertions;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

public class VersionDtoTest
{
    [Test]
    public void EqualsAndHashCode_ShouldTreatSameVersionValuesAsEqual()
    {
        var left = new VersionDto(1, 4);
        var right = new VersionDto(1, 4);

        (left == right).Should().BeTrue();
        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Test]
    public void FromString_ShouldParseValidVersionNumbers()
    {
        var result = VersionDto.FromString("2.7");

        result.Should().BeEquivalentTo(new VersionDto(2, 7));
    }

    [Test]
    public void FromString_ShouldThrowForInvalidVersionStrings()
    {
        var action = () => VersionDto.FromString("2");

        action.Should().Throw<ArgumentException>();
    }

    [Test]
    public void SharedDtos_ShouldExposeSafeDefaults_ForOptionalCollectionsAndMetadata()
    {
        var processInfo = new ProcessInstanceInfoDto
        {
            InstanceId = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            RelatedDefinitionId = "definition",
            RelatedDefinitionName = "Definition"
        };

        var extendedSubscription = new ExtendedUserTaskSubscriptionDto
        {
            Id = Guid.NewGuid(),
            Name = "Approve",
            Token = new TokenDto(),
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_1"
        };

        processInfo.Tokens.Should().BeEmpty();
        extendedSubscription.DefinitionMetaName.Should().BeEmpty();
        extendedSubscription.DefinitionVersion.Should().BeEquivalentTo(new VersionDto());
    }
}
