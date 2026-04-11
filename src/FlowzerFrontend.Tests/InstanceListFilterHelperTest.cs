using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class InstanceListFilterHelperTest
{
    [Test]
    public void ParseOrDefault_ShouldFallbackToAll_ForUnknownValues()
    {
        var result = InstanceListFilterHelper.ParseOrDefault("unexpected");

        result.Should().Be(InstanceListFilter.All);
    }

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
