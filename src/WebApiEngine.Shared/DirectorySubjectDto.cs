namespace WebApiEngine.Shared;

/// <summary>Öffentliche, typisierte Referenz auf eine bekannte Verzeichnisidentität.</summary>
public sealed class SubjectRefDto
{
    /// <summary><c>user</c> oder <c>group</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>Stabile lokale Flowzer-ID, nicht Anzeigename oder E-Mail-Adresse.</summary>
    public required Guid Id { get; set; }
}

/// <summary>Ein Suchtreffer mit unterscheidbarer Anzeige, aber ohne Provider-Credentials.</summary>
public sealed class DirectorySubjectDto
{
    public required SubjectRefDto Subject { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>Bei Benutzern der stabile Subject-Wert, bei Gruppen der vollständige Pfad.</summary>
    public required string Detail { get; set; }

    /// <summary>Ist die Identität im zuletzt vollständig publizierten Stand aktiv?</summary>
    public required bool IsActive { get; set; }

    /// <summary>
    /// Darf diese Projektion im aktuellen fachlichen Kontext neu ausgewählt werden?
    /// Historische Anzeigeauflösungen setzen diesen Wert immer geschlossen auf false,
    /// sobald Status oder aktuelle Auswahlpolicy die Referenz ausschließen.
    /// </summary>
    public required bool IsSelectable { get; set; }
}

/// <summary>Begrenzte Treffer eines einzelnen, atomar veröffentlichten Snapshots.</summary>
public sealed class DirectorySubjectSearchResultDto
{
    public required Guid GenerationId { get; set; }
    public List<DirectorySubjectDto> Items { get; set; } = [];
}

/// <summary>Begrenzte Menge bereits bekannter stabiler Referenzen zur Anzeigeauflösung.</summary>
public sealed class DirectorySubjectResolutionRequestDto
{
    public List<SubjectRefDto> Subjects { get; set; } = [];
}

/// <summary>Exakte Treffer eines einzelnen atomar publizierten Snapshots.</summary>
public sealed class DirectorySubjectResolutionResultDto
{
    public required Guid GenerationId { get; set; }
    public List<DirectorySubjectDto> Items { get; set; } = [];
}
