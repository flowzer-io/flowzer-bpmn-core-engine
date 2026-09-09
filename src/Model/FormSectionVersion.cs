namespace Model;

/// <summary>
/// Unveraenderliche, veroeffentlichte Fassung eines Formularabschnitts. Bereits gespeicherte
/// Fassungen werden weder aktualisiert noch geloescht.
/// </summary>
public sealed record FormSectionVersion(Guid Id, Guid SectionId, Version Version, string SectionData);
