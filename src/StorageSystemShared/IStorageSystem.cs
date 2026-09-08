namespace StorageSystem;

public interface IStorageSystem
{
    /// <summary>
    /// MetaDefinition
    ///  - Definition
    ///    + Process
    ///    + Process
    ///  + Definition
    /// </summary>
    IDefinitionStorage DefinitionStorage { get; }

    /// <summary>Ordner des Workflow-Katalogs samt der Zuweisungen, die an ihnen haengen.</summary>
    IFolderStorage FolderStorage { get; }

    IMessageSubscriptionStorage SubscriptionStorage { get; }

    IInstanceStorage InstanceStorage { get; }

    IFormStorage FormStorage { get; }

    /// <summary>Gemeinsame, revisionierte Entwuerfe fuer Katalogformulare.</summary>
    IFormAuthoringStorage FormAuthoringStorage => UnsupportedFormAuthoringStorage.Instance;

    /// <summary>Auftraege fuer externe Worker und deren Webhook-Anmeldungen.</summary>
    IServiceTaskStorage ServiceTaskStorage { get; }

    /// <summary>Persistente Wiederholungsverträge für direkte HTTP-Mutationen.</summary>
    IIdempotencyStorage IdempotencyStorage => UnsupportedIdempotencyStorage.Instance;

    /// <summary>Aktueller, atomar veröffentlichter Stand des externen Identitätsverzeichnisses.</summary>
    IIdentityDirectoryStorage IdentityDirectoryStorage => UnsupportedIdentityDirectoryStorage.Instance;

    /// <summary>Private, revisionsgeschuetzte Bearbeitungsstaende offener User-Tasks.</summary>
    IUserTaskDraftStorage UserTaskDraftStorage => UnsupportedUserTaskDraftStorage.Instance;

    /// <summary>Tatsächliche Human-Task-Bearbeiter, Revisionen und Audit-Ereignisse.</summary>
    IUserTaskLifecycleStorage UserTaskLifecycleStorage => UnsupportedUserTaskLifecycleStorage.Instance;

    /// <summary>Einmalig gebundene Human-Task-Fälligkeiten und Schedulerfortschritt.</summary>
    IUserTaskDeadlineStorage UserTaskDeadlineStorage => UnsupportedUserTaskDeadlineStorage.Instance;

    /// <summary>Dauerhafte, deduplizierte In-App-Meldungen zu Human Tasks.</summary>
    IUserTaskNotificationStorage UserTaskNotificationStorage => UnsupportedUserTaskNotificationStorage.Instance;
}
