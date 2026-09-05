namespace StorageSystem.Exceptions;

/// <summary>
/// Signalisiert, dass eine angeforderte Definition oder Metadefinition in der Ablage nicht gefunden wurde.
/// </summary>
public class DefinitionStorageNotFoundException(string message) : FileNotFoundException(message);
