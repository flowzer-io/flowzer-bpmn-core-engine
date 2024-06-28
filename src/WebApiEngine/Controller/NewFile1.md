•Definitionen (XML) werden, mit HASH als Objektname, in einem Objekt-Storage gespeichert.
•Es gibt ein Definition-Objekt, welches speichert:
- Definition_Guid (Selbst vergeben)
- Definition_ID (aus XML, immer gleich in den jeweiligen Versionen)
- Version (Minor je Speichervorgang, Major je Deployment, separate Felder). Deployed´e Versionen haben Minor immer gleich 0
- Hash der Definition
- Gespeichert von User
- CreatedOn, DeployedOn, DeployedBy
- Last_Guid (Von wo kommt die aktuelle Version)
  •⁠  ⁠Es gibt ein DefinitionMeta-Objekt, welches speichert:
- Definition_ID
- Name
- IsActive : Wenn False, wurde die ganze Definition angehalten und wird nicht "angeboten".

Funktionen der API

1.Upload Definition (fileStream Definition XML) -> DefinitionObject
1. Parsed die XML
2. Schaut, ob es bereits Einträge gibt mit der gleichen Definitions_ID
3. Version ist gleich max alte Version + Minor 1 (oder 0.1)
4. Speichert die Datei im Storage unter dem Filehash als Dateinamen, und speichert die Info
   2.Change Name (Definition_ID, NewName) -> 200 OK
   3.Deploy Definition (DefinitionGuid) -> DefinitionObject
1. Die Definition wird kopiert mit einer neuen Major Version (Minor 0) und neuer Guid
2. Vorherige Major-Version wird "Deaktiviert"
3. Neue Major Version wird "Aktiviert"
4. Möglichst als Atomarer Vorgang
   4.Delete Definition (DefinitionGuid) -> 200 OK
1. Geht nur, wenn es keine Instanzen der Definition gibt.
2. Lösche die XML Datei
3. Wenn Minor 0: Deaktiviere die aktuelle Definition,
4. Wenn Minor 0: Aktiviere, falls vorhanden, die letzte Major Version mit Minor 0
   5.SetState (Definition_ID, bool Active) -> 200 OK
1. (De-)aktiviere die aktuell Deployed´te Definition