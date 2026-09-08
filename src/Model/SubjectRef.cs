namespace Model;

/// <summary>Art einer bekannten, lokal aufgelösten Verzeichnisidentität.</summary>
public enum DirectorySubjectKind
{
    User = 0,
    Group = 1
}

/// <summary>
/// Stabile Referenz auf einen Benutzer oder eine Gruppe des lokalen Verzeichnis-Snapshots.
/// Die ID ist eine Flowzer-ID und bleibt über Provider-Synchronisationen erhalten.
/// Freitext ist bewusst kein dritter Typ: Legacy-Zuweisungen bleiben ein eigener Vertrag.
/// </summary>
public sealed record SubjectRef(DirectorySubjectKind Kind, Guid Id);
