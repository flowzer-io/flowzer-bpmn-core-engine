using System.Dynamic;
using FluentAssertions;
using FlowzerFrontend.BusinessLogic;
using Microsoft.FluentUI.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Tests;

public class InstanceTreeViewBuilderTest
{
    // Testzweck: Prüft, dass die Token-Hierarchie der Instanzansicht in eine verschachtelte Tree-View-Struktur mit Root- und Child-Knoten übersetzt wird.
    [Test]
    public void BuildTokenItems_ShouldBuildNestedTree_ForRootAndChildTokens()
    {
        dynamic rootFlowElement = new ExpandoObject();
        rootFlowElement.Name = "Approve invoice";

        dynamic childFlowElement = new ExpandoObject();
        childFlowElement.Id = "Activity_Child";

        var rootTokenId = Guid.NewGuid();
        var rootToken = new TokenDto
        {
            Id = rootTokenId,
            CurrentFlowElement = rootFlowElement
        };

        var childToken = new TokenDto
        {
            Id = Guid.NewGuid(),
            ParentTokenId = rootTokenId,
            CurrentFlowElement = childFlowElement
        };

        var result = InstanceTreeViewBuilder.BuildTokenItems([rootToken, childToken]);

        result.Should().HaveCount(1);
        var rootItem = result.Single().Should().BeOfType<TreeViewItem>().Subject;
        rootItem.Text.Should().Be("Approve invoice");
        rootItem.Items.Should().NotBeNull();
        rootItem.Items!.Should().HaveCount(1);
        rootItem.Items!.Single().Should().BeOfType<TreeViewItem>().Which.Text.Should().Be("Activity_Child");
    }

    // Testzweck: Prüft, dass die Übersichtsgruppen nur dann Platzhalter laden, wenn die jeweilige Subscription-Kategorie auch Einträge besitzt.
    [Test]
    public void BuildSubscriptionOverview_ShouldSetLoadingItemsOnly_ForCategoriesWithEntries()
    {
        var instance = new ProcessInstanceInfoDto
        {
            InstanceId = Guid.NewGuid(),
            DefinitionId = Guid.NewGuid(),
            RelatedDefinitionId = "Definition_1",
            RelatedDefinitionName = "Example",
            MessageSubscriptionCount = 2,
            ServiceSubscriptionCount = 0,
            SignalSubscriptionCount = 1,
            UserTaskSubscriptionCount = 0,
            Tokens = []
        };

        var result = InstanceTreeViewBuilder.BuildSubscriptionOverview(instance);

        result.Should().HaveCount(4);
        result[0].Text.Should().Be("Message subscriptions (2)");
        result[0].Items.Should().NotBeNullOrEmpty();
        result[1].Text.Should().Be("Service subscriptions (0)");
        result[1].Items.Should().BeNull();
        result[2].Text.Should().Be("Signal subscriptions (1)");
        result[2].Items.Should().NotBeNullOrEmpty();
        result[3].Text.Should().Be("Usertask subscriptions (0)");
        result[3].Items.Should().BeNull();
    }

    // Testzweck: Prüft, dass Message-Subscriptions stabile Tree-Items erzeugen und die Originalobjekte für spätere Aktionen im Mapping abgelegt werden.
    [Test]
    public void BuildMessageSubscriptionItems_ShouldReturnTreeItems_AndPopulateMappings()
    {
        var mapping = new Dictionary<string, object>();
        var subscription = new MessageSubscriptionDto
        {
            Message = new MessageDefinitionDto { Name = "InvoiceReceived" },
            ProcessId = "Process_1",
            RelatedDefinitionId = "Definition_1",
            DefinitionId = Guid.NewGuid(),
            ProcessInstanceId = Guid.NewGuid()
        };

        var result = InstanceTreeViewBuilder.BuildMessageSubscriptionItems([subscription], mapping);

        result.Should().HaveCount(1);
        result.Single().Should().BeOfType<TreeViewItem>().Which.Id.Should().Be("message_InvoiceReceived");
        mapping.Should().ContainKey("message_InvoiceReceived");
        mapping["message_InvoiceReceived"].Should().BeSameAs(subscription);
    }

    // Testzweck: Prüft, dass bekannte Tree-IDs zuverlässig in fachliche Subscription-Kategorien übersetzt werden.
    [TestCase("messages", InstanceSubscriptionTreeCategory.Messages)]
    [TestCase("service", InstanceSubscriptionTreeCategory.Services)]
    [TestCase("signal", InstanceSubscriptionTreeCategory.Signals)]
    [TestCase("user", InstanceSubscriptionTreeCategory.UserTasks)]
    public void TryParseSubscriptionCategory_ShouldParseKnownIds(
        string itemId,
        InstanceSubscriptionTreeCategory expectedCategory)
    {
        var success = InstanceTreeViewBuilder.TryParseSubscriptionCategory(itemId, out var category);

        success.Should().BeTrue();
        category.Should().Be(expectedCategory);
    }

    // Testzweck: Prüft, dass unbekannte Tree-IDs defensiv abgelehnt werden, damit die UI keine undefinierte Kategorie verarbeitet.
    [Test]
    public void TryParseSubscriptionCategory_ShouldRejectUnknownIds()
    {
        var success = InstanceTreeViewBuilder.TryParseSubscriptionCategory("unknown", out _);

        success.Should().BeFalse();
    }
}
