namespace WebApiEngine.Shared;

/// <summary>
/// Das Manifest eines Prozesspakets (<c>package.json</c>). Es beschreibt genau einen Workflow
/// samt der Formulare, die an die exportierte Fassung gebunden sind, und nennt alles, was beim
/// Import ausdruecklich zugeordnet werden muss.
///
/// Das Manifest enthaelt niemals Secrets, Tokens, Instanzen, Aufgaben, Historie oder
/// Personenkennungen. Verzeichnisbezuege stehen ausschliesslich mit ihrem Anzeigenamen darin.
/// </summary>
public sealed class ProcessPackageManifestDto
{
    /// <summary>Immer <c>flowzer.process-package/1</c>; ein anderer Wert wird abgelehnt.</summary>
    public required string Format { get; init; }

    public required int FormatVersion { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    /// <summary>Die Fassung der exportierenden Installation — rein informativ.</summary>
    public required string FlowzerVersion { get; init; }

    /// <summary>Version des BPMN-Faehigkeitsvertrags, gegen den das Modell geprueft wurde.</summary>
    public required int BpmnCapabilitiesContract { get; init; }

    /// <summary>Profil des Formularvertrags, etwa <c>flowzer.forms/4</c>.</summary>
    public required string FormsContract { get; init; }

    public required ProcessPackageWorkflowDto Workflow { get; init; }

    public required ProcessPackageFormDto[] Forms { get; init; }

    public required ProcessPackageReferenceDto[] References { get; init; }
}

/// <summary>Der exportierte Workflow. <see cref="Source"/> unterscheidet Veroeffentlichung und Entwurf.</summary>
public sealed class ProcessPackageWorkflowDto
{
    public required string DefinitionId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Die Fassung als <c>Major.Minor</c>.</summary>
    public required string Version { get; init; }

    /// <summary>Die Prozesse im Modell; der erste ist der Hauptprozess.</summary>
    public required string[] ProcessIds { get; init; }

    /// <summary>
    /// <c>deployed</c>, wenn die veroeffentlichte Fassung exportiert wurde, sonst <c>draft</c>.
    /// Ein Entwurf hat keine unveraenderlichen Formularbindungen; seine Formularstaende wurden
    /// beim Export so aufgeloest, wie eine Veroeffentlichung es getan haette.
    /// </summary>
    public required string Source { get; init; }
}

/// <summary>Ein Formularstand, den der Workflow braucht.</summary>
public sealed class ProcessPackageFormDto
{
    /// <summary>
    /// Die stabile Formularkennung aus dem Bestand. <c>null</c> bei einem Formular, das im
    /// Diagramm selbst liegt — es reist in <c>workflow.bpmn</c> mit.
    /// </summary>
    public Guid? FormId { get; init; }

    public required string Name { get; init; }

    /// <summary>Die Fassung des Formulars, etwa <c>1.0</c>; <c>null</c> bei eingebetteten Formularen.</summary>
    public string? Revision { get; init; }

    /// <summary>Der Form-Key, unter dem das Modell dieses Formular anspricht.</summary>
    public required string FormKey { get; init; }

    /// <summary>Der Pfad der Formulardatei im Paket, etwa <c>forms/&lt;formId&gt;.json</c>.</summary>
    public required string File { get; init; }

    /// <summary>Ein Formular aus dem Diagramm; der Import legt dafuer keinen Katalogeintrag an.</summary>
    public required bool Embedded { get; init; }
}

/// <summary>Die Arten installationsgebundener Bezuege, die ein Paket nennen kann.</summary>
public static class ProcessPackageReferenceKinds
{
    /// <summary>Eine Person aus dem Verzeichnis; im Paket steht nur ihr Anzeigename.</summary>
    public const string DirectoryUser = "directoryUser";

    /// <summary>Eine Gruppe aus dem Verzeichnis; im Paket steht nur ihr Anzeigename.</summary>
    public const string DirectoryGroup = "directoryGroup";

    /// <summary>Eine KI-Verbindung an einem KI-Task; im Paket steht nur ihr Name.</summary>
    public const string AiConnection = "aiConnection";

    /// <summary>Der Auftragstyp eines Service-Tasks. Nur informativ: „Worker fuer Typ X noetig“.</summary>
    public const string JobType = "jobType";

    /// <summary>Der Name einer Secret-Referenz (<c>secret:NAME</c>). Nur der Name, nie der Wert.</summary>
    public const string Secret = "secret";

    /// <summary>Ein aufgerufener Prozess (<c>zeebe:calledElement</c>). Nur nennen.</summary>
    public const string CalledProcess = "calledProcess";

    /// <summary>Eine aufgerufene Entscheidung (<c>zeebe:calledDecision</c>). Nur nennen.</summary>
    public const string CalledDecision = "calledDecision";
}

/// <summary>
/// Ein Bezug, den die Zielinstallation selbst aufloesen muss.
///
/// Bezuege mit <see cref="RequiresMapping"/> stehen im mitgelieferten <c>workflow.bpmn</c> als
/// Platzhalter <c>flowzer:ref:&lt;id&gt;</c>. Erst der Import ersetzt sie durch die Kennungen der
/// Zielinstallation. Ohne diesen Umweg stuenden fremde Personen- und Verbindungskennungen im
/// Paket — genau das soll ein Paket nicht weitergeben.
/// </summary>
public sealed class ProcessPackageReferenceDto
{
    /// <summary>Stabile, paketlokale Kennung des Bezugs; der Schluessel der Zuordnung.</summary>
    public required string Id { get; init; }

    /// <summary>Eine der Arten aus <see cref="ProcessPackageReferenceKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Der BPMN-Knoten, an dem der Bezug haengt.</summary>
    public required string ElementId { get; init; }

    /// <summary>Der Name des Knotens, soweit das Modell einen traegt.</summary>
    public string? ElementName { get; init; }

    /// <summary>
    /// Was im Quellsystem gemeint war: der Anzeigename einer Person oder Gruppe, der Name einer
    /// KI-Verbindung, ein Auftragstyp, ein Secret-Name oder eine Prozess-/Entscheidungskennung.
    /// Niemals eine Personenkennung und niemals ein geheimer Wert.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Ob der Import ohne eine Zuordnung nicht auskommt. <c>false</c> heisst: nur ein Hinweis,
    /// etwa „fuer diesen Auftragstyp muss ein Worker bereitstehen“.
    /// </summary>
    public required bool RequiresMapping { get; init; }
}

/// <summary>Ein Vorschlag fuer einen Bezug: ein gleichnamiger Eintrag der Zielinstallation.</summary>
public sealed class ProcessPackageCandidateDto
{
    public required string Id { get; init; }
    public required string Label { get; init; }

    /// <summary>Zusatz zur Unterscheidung, etwa der Gruppenpfad oder das Standardmodell.</summary>
    public string? Hint { get; init; }
}

/// <summary>Ein Bezug samt dem, was die Zielinstallation dafuer anbieten kann.</summary>
public sealed class ProcessPackageReferenceOptionsDto
{
    public required ProcessPackageReferenceDto Reference { get; init; }

    /// <summary>Die Auswahl, aus der zugeordnet werden kann; leer bei rein informativen Bezuegen.</summary>
    public required ProcessPackageCandidateDto[] Candidates { get; init; }

    /// <summary>Der vorbelegte Vorschlag — ein gleichnamiger Eintrag; sonst <c>null</c>.</summary>
    public string? SuggestedId { get; init; }
}

/// <summary>Ein Befund der Paketpruefung. Der Code ist stabil, die Meldung erklaert ihn.</summary>
public sealed class ProcessPackageFindingDto
{
    public required string Code { get; init; }
    public required string Message { get; init; }

    /// <summary>Der betroffene BPMN-Knoten, soweit der Befund einem zuzuordnen ist.</summary>
    public string? ElementId { get; init; }
}

/// <summary>Was ein bereits vorhandener Katalogeintrag fuer den Import bedeutet.</summary>
public sealed class ProcessPackageConflictDto
{
    /// <summary>Die Kennung, die im Zielsystem bereits vergeben ist.</summary>
    public required string DefinitionId { get; init; }

    public required string Name { get; init; }

    /// <summary>Die hoechste vorhandene Fassung, etwa <c>2.0</c>; <c>null</c> ohne Version.</summary>
    public string? LatestVersion { get; init; }

    /// <summary>Ob der Aufrufer in dem Ordner, in dem der Eintrag liegt, aendern darf.</summary>
    public required bool MayCreateNewVersion { get; init; }
}

/// <summary>
/// Das Ergebnis der Vorschau: was im Paket steht, ob die Zielinstallation es annehmen wuerde,
/// welche Bezuege zuzuordnen sind und was einer vorhandenen Kennung entgegensteht.
/// </summary>
public sealed class ProcessPackagePreviewDto
{
    public required ProcessPackageManifestDto Manifest { get; init; }

    /// <summary>Ob eine Veroeffentlichung das Modell gegen den aktuellen Vertrag annaehme.</summary>
    public required bool DeployableHere { get; init; }

    /// <summary>Ob das Formularprofil des Pakets von dieser Installation unterstuetzt wird.</summary>
    public required bool FormsContractSupported { get; init; }

    /// <summary>Gruende, aus denen die Veroeffentlichung hier scheitern wuerde.</summary>
    public required ProcessPackageFindingDto[] Problems { get; init; }

    /// <summary>Hinweise, die den Import nicht verhindern.</summary>
    public required ProcessPackageFindingDto[] Notices { get; init; }

    public required ProcessPackageReferenceOptionsDto[] References { get; init; }

    /// <summary>Gesetzt, wenn die Kennung des Pakets hier schon vergeben ist.</summary>
    public ProcessPackageConflictDto? Conflict { get; init; }
}

/// <summary>Die Zielentscheidung und die Zuordnungen, mit denen importiert wird.</summary>
public sealed class ProcessPackageMappingDto
{
    /// <summary><c>new</c> legt einen neuen Workflow an, <c>newVersionOf</c> einen neuen Stand.</summary>
    public required string Mode { get; init; }

    /// <summary>Bei <c>newVersionOf</c> der vorhandene Katalogeintrag; sonst die gewuenschte neue Kennung.</summary>
    public string? DefinitionId { get; init; }

    /// <summary>Nur bei <c>new</c>: der Zielordner; ohne Wert die oberste Ebene.</summary>
    public Guid? FolderId { get; init; }

    /// <summary>Nur bei <c>new</c>: der Name im Katalog; ohne Wert der Name aus dem Paket.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Je Bezugskennung die Kennung im Zielsystem. Ein Bezug, der eine Zuordnung braucht, muss
    /// hier stehen; rein informative Bezuege werden ignoriert.
    /// </summary>
    public Dictionary<string, string>? References { get; init; }
}

/// <summary>Was ein Formular beim Import geworden ist.</summary>
public sealed class ProcessPackageImportedFormDto
{
    public required string FormKey { get; init; }
    public required string Name { get; init; }

    /// <summary>Die Kennung im Zielsystem; <c>null</c> bei einem eingebetteten Formular.</summary>
    public Guid? FormId { get; init; }

    /// <summary>Die angelegte oder wiederverwendete Fassung, etwa <c>1.0</c>.</summary>
    public string? Revision { get; init; }

    /// <summary>
    /// <c>created</c> (neues Formular), <c>reused</c> (gleiche Kennung, gleicher Inhalt),
    /// <c>revised</c> (gleiche Kennung, anderer Inhalt) oder <c>embedded</c>.
    /// </summary>
    public required string Outcome { get; init; }
}

/// <summary>Der Bericht des Imports: was entstanden ist und was noch zu tun bleibt.</summary>
public sealed class ProcessPackageImportResultDto
{
    public required string DefinitionId { get; init; }
    public required string Name { get; init; }

    /// <summary>Die Kennung der angelegten Fassung.</summary>
    public required Guid VersionId { get; init; }

    public required VersionDto Version { get; init; }

    public required ProcessPackageImportedFormDto[] Forms { get; init; }

    /// <summary>Die angewandten Zuordnungen, je Bezugskennung der gewaehlte Zielwert.</summary>
    public required ProcessPackageReferenceDto[] AppliedReferences { get; init; }

    /// <summary>Was der Import nicht selbst erledigt — vor allem die noetige Veroeffentlichung.</summary>
    public required ProcessPackageFindingDto[] Notices { get; init; }
}
