using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using Microsoft.FluentUI.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class ModelListViewHelperTest
{
    // Testzweck: Deckt den Fall „Apply Query Should Filter Across Name Definition ID And Description“ ab.
    [Test]
    public void ApplyQuery_ShouldFilterAcrossNameDefinitionIdAndDescription()
    {
        var models = new[]
        {
            new ExtendedBpmnMetaDefinitionDto
            {
                DefinitionId = "invoice-process",
                Name = "Invoice",
                Description = "Handles invoices"
            },
            new ExtendedBpmnMetaDefinitionDto
            {
                DefinitionId = "shipment-process",
                Name = "Shipment",
                Description = "Ships packages"
            }
        };

        var result = ModelListViewHelper.ApplyQuery(models, "invoice", SortDirection.Ascending).ToArray();

        result.Should().ContainSingle();
        result[0].DefinitionId.Should().Be("invoice-process");
    }

    // Testzweck: Deckt den Fall „Apply Query Should Sort Ascending By Name“ ab.
    [Test]
    public void ApplyQuery_ShouldSortAscendingByName()
    {
        var models = new[]
        {
            CreateModel("zeta", "Zeta"),
            CreateModel("alpha", "Alpha")
        };

        var result = ModelListViewHelper.ApplyQuery(models, null, SortDirection.Ascending).ToArray();

        result.Select(model => model.Name).Should().Equal("Alpha", "Zeta");
    }

    // Testzweck: Deckt den Fall „Apply Query Should Sort Descending By Name“ ab.
    [Test]
    public void ApplyQuery_ShouldSortDescendingByName()
    {
        var models = new[]
        {
            CreateModel("zeta", "Zeta"),
            CreateModel("alpha", "Alpha")
        };

        var result = ModelListViewHelper.ApplyQuery(models, null, SortDirection.Descending).ToArray();

        result.Select(model => model.Name).Should().Equal("Zeta", "Alpha");
    }

    private static ExtendedBpmnMetaDefinitionDto CreateModel(string definitionId, string name)
    {
        return new ExtendedBpmnMetaDefinitionDto
        {
            DefinitionId = definitionId,
            Name = name
        };
    }
}
