namespace FilesystemStorageSystem.Exceptions;

/// <summary>
/// Signalisiert einen Konflikt beim Persistieren von Definitionen oder Metadaten,
/// zum Beispiel wenn ein Datensatz bereits vorhanden ist.
/// </summary>
public class DefinitionStorageConflictException(string message) : InvalidOperationException(message);
