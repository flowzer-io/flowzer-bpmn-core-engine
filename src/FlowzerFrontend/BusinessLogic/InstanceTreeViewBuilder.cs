using Microsoft.FluentUI.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Kapselt den Aufbau der Tree-View-Struktur für die Instanzansicht,
/// damit die Blazor-Seite nur noch Rendering und Orchestrierung übernimmt.
/// </summary>
public static class InstanceTreeViewBuilder
{
    /// <summary>
    /// Baut aus den Tokens einer Instanz eine verschachtelte Tree-View-Struktur.
    /// </summary>
    public static IReadOnlyList<ITreeViewItem> BuildTokenItems(IEnumerable<TokenDto> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var allTokens = tokens.ToList();
        var rootTokens = allTokens
            .Where(token => token.ParentTokenId == null || token.ParentTokenId == Guid.Empty)
            .ToList();

        if (rootTokens.Count == 0)
        {
            return [];
        }

        return BuildTokenItems(allTokens, rootTokens);
    }

    /// <summary>
    /// Erstellt die vier festen Subscription-Gruppen inklusive Ladeplatzhaltern.
    /// </summary>
    public static IReadOnlyList<TreeViewItem> BuildSubscriptionOverview(ProcessInstanceInfoDto instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return
        [
            CreateSubscriptionOverviewItem(InstanceSubscriptionTreeCategory.Messages, instance.MessageSubscriptionCount),
            CreateSubscriptionOverviewItem(InstanceSubscriptionTreeCategory.Services, instance.ServiceSubscriptionCount),
            CreateSubscriptionOverviewItem(InstanceSubscriptionTreeCategory.Signals, instance.SignalSubscriptionCount),
            CreateSubscriptionOverviewItem(InstanceSubscriptionTreeCategory.UserTasks, instance.UserTaskSubscriptionCount)
        ];
    }

    /// <summary>
    /// Übersetzt eine Tree-Item-ID der Instanzansicht in die zugehörige Subscription-Kategorie.
    /// </summary>
    public static bool TryParseSubscriptionCategory(
        string? itemId,
        out InstanceSubscriptionTreeCategory category)
    {
        switch (itemId)
        {
            case "messages":
                category = InstanceSubscriptionTreeCategory.Messages;
                return true;
            case "service":
                category = InstanceSubscriptionTreeCategory.Services;
                return true;
            case "signal":
                category = InstanceSubscriptionTreeCategory.Signals;
                return true;
            case "user":
                category = InstanceSubscriptionTreeCategory.UserTasks;
                return true;
            default:
                category = default;
                return false;
        }
    }

    /// <summary>
    /// Baut Tree-Items für Message-Subscriptions und merkt sich die Originalobjekte für Folgeaktionen.
    /// </summary>
    public static IReadOnlyList<ITreeViewItem> BuildMessageSubscriptionItems(
        IEnumerable<MessageSubscriptionDto> subscriptions,
        IDictionary<string, object> treeItemMappings)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(treeItemMappings);

        return subscriptions.Select(subscription =>
            {
                var id = "message_" + subscription.Message.Name;
                treeItemMappings[id] = subscription;
                return (ITreeViewItem)new TreeViewItem(id, subscription.Message.Name);
            })
            .ToList();
    }

    /// <summary>
    /// Baut Tree-Items für aktive Service-Task-Subscriptions.
    /// </summary>
    public static IReadOnlyList<ITreeViewItem> BuildServiceSubscriptionItems(IEnumerable<TokenDto> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        return subscriptions.Select(subscription =>
            {
                var implementationName = TokenDisplayHelper.GetImplementation(subscription)
                                         ?? subscription.CurrentFlowNodeId
                                         ?? "(service task)";
                return (ITreeViewItem)new TreeViewItem(subscription.Id.ToString(), implementationName);
            })
            .ToList();
    }

    /// <summary>
    /// Baut Tree-Items für aktive Signal-Subscriptions.
    /// </summary>
    public static IReadOnlyList<ITreeViewItem> BuildSignalSubscriptionItems(IEnumerable<SignalSubscriptionDto> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        return subscriptions.Select(subscription =>
                (ITreeViewItem)new TreeViewItem(subscription.Signal, subscription.Signal))
            .ToList();
    }

    /// <summary>
    /// Baut Tree-Items für aktive User-Tasks.
    /// </summary>
    public static IReadOnlyList<ITreeViewItem> BuildUserTaskSubscriptionItems(IEnumerable<TokenDto> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        return subscriptions.Select(subscription =>
            {
                var implementationName = TokenDisplayHelper.GetImplementation(subscription)
                                         ?? subscription.CurrentFlowNodeId
                                         ?? "(user task)";
                return (ITreeViewItem)new TreeViewItem(subscription.Id.ToString(), implementationName);
            })
            .ToList();
    }

    private static IReadOnlyList<ITreeViewItem> BuildTokenItems(
        IReadOnlyCollection<TokenDto> allTokens,
        IReadOnlyCollection<TokenDto> currentLevelTokens)
    {
        var result = new List<ITreeViewItem>(currentLevelTokens.Count);

        foreach (var token in currentLevelTokens)
        {
            var subTokens = allTokens.Where(candidate => candidate.ParentTokenId == token.Id).ToList();
            var subItems = BuildTokenItems(allTokens, subTokens);

            result.Add(new TreeViewItem
            {
                Id = token.Id.ToString(),
                Text = TokenDisplayHelper.GetDisplayName(token, "(root token)"),
                Items = subItems,
                Expanded = true
            });
        }

        return result;
    }

    private static TreeViewItem CreateSubscriptionOverviewItem(
        InstanceSubscriptionTreeCategory category,
        int count)
    {
        var item = new TreeViewItem
        {
            Id = GetSubscriptionItemId(category),
            Text = GetSubscriptionTitle(category, count)
        };

        if (count > 0)
        {
            item.Items = TreeViewItem.LoadingTreeViewItems;
        }

        return item;
    }

    private static string GetSubscriptionItemId(InstanceSubscriptionTreeCategory category)
    {
        return category switch
        {
            InstanceSubscriptionTreeCategory.Messages => "messages",
            InstanceSubscriptionTreeCategory.Services => "service",
            InstanceSubscriptionTreeCategory.Signals => "signal",
            InstanceSubscriptionTreeCategory.UserTasks => "user",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported subscription category.")
        };
    }

    private static string GetSubscriptionTitle(InstanceSubscriptionTreeCategory category, int count)
    {
        return category switch
        {
            InstanceSubscriptionTreeCategory.Messages => $"Message subscriptions ({count})",
            InstanceSubscriptionTreeCategory.Services => $"Service subscriptions ({count})",
            InstanceSubscriptionTreeCategory.Signals => $"Signal subscriptions ({count})",
            InstanceSubscriptionTreeCategory.UserTasks => $"Usertask subscriptions ({count})",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported subscription category.")
        };
    }
}
