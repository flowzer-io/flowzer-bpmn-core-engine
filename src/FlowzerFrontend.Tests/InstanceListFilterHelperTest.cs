using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class InstanceListFilterHelperTest
{
    // Testzweck: Deckt den Fall „Parse Or Default Should Fallback To All For Unknown Values“ ab.
    [Test]
    public void ParseOrDefault_ShouldFallbackToAll_ForUnknownValues()
    {
        var result = InstanceListFilterHelper.ParseOrDefault("unexpected");

        result.Should().Be(InstanceListFilter.All);
    }

    // Testzweck: Deckt den Fall „Apply Should Return Only Active States For Active Filter“ ab.
    [Test]
    public void Apply_ShouldReturnOnlyActiveStates_ForActiveFilter()
    {
        var instances = new[]
        {
            CreateInstance(ProcessInstanceStateDto.Running),
            CreateInstance(ProcessInstanceStateDto.Waiting),
            CreateInstance(ProcessInstanceStateDto.Completed),
            CreateInstance(ProcessInstanceStateDto.Failed)
        };

        var result = InstanceListFilterHelper.Apply(instances, InstanceListFilter.Active).ToArray();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(instance =>
            instance.State == ProcessInstanceStateDto.Running ||
            instance.State == ProcessInstanceStateDto.Waiting);
    }

    // Testzweck: Deckt den Fall „Apply Should Return Only Done States For Done Filter“ ab.
    [Test]
    public void Apply_ShouldReturnOnlyDoneStates_ForDoneFilter()
    {
        var instances = new[]
        {
            CreateInstance(ProcessInstanceStateDto.Completed),
            CreateInstance(ProcessInstanceStateDto.Terminated),
            CreateInstance(ProcessInstanceStateDto.Running)
        };

        var result = InstanceListFilterHelper.Apply(instances, InstanceListFilter.Done).ToArray();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(instance =>
            instance.State == ProcessInstanceStateDto.Completed ||
            instance.State == ProcessInstanceStateDto.Terminated);
    }

    // Testzweck: Deckt den Fall „Apply Should Return Only Error States For Error Filter“ ab.
    [Test]
    public void Apply_ShouldReturnOnlyErrorStates_ForErrorFilter()
    {
        var instances = new[]
        {
            CreateInstance(ProcessInstanceStateDto.Failing),
            CreateInstance(ProcessInstanceStateDto.Failed),
            CreateInstance(ProcessInstanceStateDto.Compensating)
        };

        var result = InstanceListFilterHelper.Apply(instances, InstanceListFilter.Error).ToArray();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(instance =>
            instance.State == ProcessInstanceStateDto.Failing ||
            instance.State == ProcessInstanceStateDto.Failed);
    }

    // Testzweck: Deckt den Fall „To Route Segment Should Return Stable Segments“ ab.
    [Test]
    public void ToRouteSegment_ShouldReturnStableSegments()
    {
        InstanceListFilterHelper.ToRouteSegment(InstanceListFilter.All).Should().Be("all");
        InstanceListFilterHelper.ToRouteSegment(InstanceListFilter.Active).Should().Be("active");
        InstanceListFilterHelper.ToRouteSegment(InstanceListFilter.Done).Should().Be("done");
        InstanceListFilterHelper.ToRouteSegment(InstanceListFilter.Error).Should().Be("error");
    }

    private static ProcessInstanceInfoDto CreateInstance(ProcessInstanceStateDto state)
    {
        return new ProcessInstanceInfoDto
        {
            InstanceId = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            RelatedDefinitionId = "definition",
            RelatedDefinitionName = "Demo process",
            State = state
        };
    }
}
