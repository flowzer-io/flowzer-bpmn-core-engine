using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class InstanceListViewHelperTest
{
    // Testzweck: Prüft, dass die kombinierte Listenlogik zuerst nach Laufzeitfilter und anschließend nach Suchtext einschränkt.
    [Test]
    public void ApplyQuery_ShouldCombineFilterAndSearch()
    {
        var matchingInstanceId = Guid.NewGuid();
        var instances = new[]
        {
            CreateInstance("Invoice approval", "invoice-approval", matchingInstanceId, ProcessInstanceStateDto.Running),
            CreateInstance("Invoice archive", "invoice-archive", Guid.NewGuid(), ProcessInstanceStateDto.Completed),
            CreateInstance("Vacation request", "vacation-request", Guid.NewGuid(), ProcessInstanceStateDto.Running)
        };

        var result = InstanceListViewHelper.ApplyQuery(instances, InstanceListFilter.Active, "invoice").ToArray();

        result.Should().ContainSingle();
        result[0].InstanceId.Should().Be(matchingInstanceId);
    }

    // Testzweck: Prüft, dass die Instanzliste für eine stabile, gut lesbare Oberfläche alphabetisch nach Workflownamen sortiert wird.
    [Test]
    public void ApplyQuery_ShouldSortAlphabeticallyByWorkflowName()
    {
        var instances = new[]
        {
            CreateInstance("Zeta workflow", "zeta", Guid.NewGuid(), ProcessInstanceStateDto.Running),
            CreateInstance("Alpha workflow", "alpha", Guid.NewGuid(), ProcessInstanceStateDto.Running)
        };

        var result = InstanceListViewHelper.ApplyQuery(instances, InstanceListFilter.All, null).ToArray();

        result.Select(instance => instance.RelatedDefinitionName).Should().Equal("Alpha workflow", "Zeta workflow");
    }

    private static ProcessInstanceInfoDto CreateInstance(
        string relatedDefinitionName,
        string relatedDefinitionId,
        Guid instanceId,
        ProcessInstanceStateDto state)
    {
        return new ProcessInstanceInfoDto
        {
            InstanceId = instanceId,
            DefinitionId = Guid.NewGuid(),
            RelatedDefinitionId = relatedDefinitionId,
            RelatedDefinitionName = relatedDefinitionName,
            State = state
        };
    }
}
