using FluentAssertions;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

public class VersionDtoTest
{
    // Testzweck: Deckt den Fall „Equals And Hash Code Should Treat Same Version Values As Equal“ ab.
    [Test]
    public void EqualsAndHashCode_ShouldTreatSameVersionValuesAsEqual()
    {
        var left = new VersionDto(1, 4);
        var right = new VersionDto(1, 4);

        (left == right).Should().BeTrue();
        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    // Testzweck: Deckt den Fall „From String Should Parse Valid Version Numbers“ ab.
    [Test]
    public void FromString_ShouldParseValidVersionNumbers()
    {
        var result = VersionDto.FromString("2.7");

        result.Should().BeEquivalentTo(new VersionDto(2, 7));
    }

    // Testzweck: Deckt den Fall „From String Should Throw For Invalid Version Strings“ ab.
    [Test]
    public void FromString_ShouldThrowForInvalidVersionStrings()
    {
        var action = () => VersionDto.FromString("2");

        action.Should().Throw<ArgumentException>();
    }

    // Testzweck: Deckt den Fall „Shared DTOs Should Expose Safe Defaults For Optional Collections And Metadata“ ab.
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
