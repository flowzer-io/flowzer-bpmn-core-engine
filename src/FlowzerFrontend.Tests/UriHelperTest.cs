using FluentAssertions;

namespace FlowzerFrontend.Tests;

public class UriHelperTest
{
    // Testzweck: Deckt den Fall „Get Instances URL Should Return Base Instances Route When No Filter Is Provided“ ab.
    [Test]
    public void GetInstancesUrl_ShouldReturnBaseInstancesRoute_WhenNoFilterIsProvided()
    {
        var result = FlowzerFrontend.UriHelper.GetInstancesUrl();

        result.Should().Be("/instances");
    }

    // Testzweck: Deckt den Fall „Get Instances URL Should Append Filter Segment When Filter Is Provided“ ab.
    [Test]
    public void GetInstancesUrl_ShouldAppendFilterSegment_WhenFilterIsProvided()
    {
        var result = FlowzerFrontend.UriHelper.GetInstancesUrl("error");

        result.Should().Be("/instances/error");
    }
}
