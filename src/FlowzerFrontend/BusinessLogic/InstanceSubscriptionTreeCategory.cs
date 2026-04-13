namespace FlowzerFrontend.BusinessLogic;

/// <summary>
/// Beschreibt die vier Subscription-Gruppen der Instanzansicht.
/// </summary>
public enum InstanceSubscriptionTreeCategory
{
    /// <summary>
    /// Gruppiert Message-Subscriptions.
    /// </summary>
    Messages,

    /// <summary>
    /// Gruppiert Service-Task-Subscriptions.
    /// </summary>
    Services,

    /// <summary>
    /// Gruppiert Signal-Subscriptions.
    /// </summary>
    Signals,

    /// <summary>
    /// Gruppiert User-Task-Subscriptions.
    /// </summary>
    UserTasks
}
