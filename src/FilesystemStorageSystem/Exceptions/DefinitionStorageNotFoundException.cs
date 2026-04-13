namespace FilesystemStorageSystem.Exceptions;

/// <summary>
/// Signalisiert, dass eine angeforderte Definition oder Metadefinition im Dateisystem nicht gefunden wurde.
/// </summary>
public class DefinitionStorageNotFoundException(string message) : FileNotFoundException(message);
