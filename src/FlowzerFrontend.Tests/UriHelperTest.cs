using FluentAssertions;

namespace FlowzerFrontend.Tests;

public class UriHelperTest
{
    [Test]
    public void GetInstancesUrl_ShouldReturnBaseInstancesRoute_WhenNoFilterIsProvided()
    {
        var result = FlowzerFrontend.UriHelper.GetInstancesUrl();

        result.Should().Be("/instances");
    }

    [Test]
    public void GetInstancesUrl_ShouldAppendFilterSegment_WhenFilterIsProvided()
    {
        var result = FlowzerFrontend.UriHelper.GetInstancesUrl("error");

        result.Should().Be("/instances/error");
    }
}
