using System.Reflection;
using System.Text;
using Model;
using StorageSystem;
using StorageSystem.Exceptions;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Forms;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using Version = Model.Version;

namespace WebApiEngine.ProcessPackages;

/// <summary>
/// Traegt einen Workflow als ein Paket aus einer Installation heraus und in eine andere hinein.
///
/// Der Export nimmt genau das mit, was der Workflow braucht: das BPMN der exportierten Fassung
/// und die Formularstaende, die daran gebunden sind. Was nur hier gilt — Personen, Gruppen,
/// KI-Verbindungen — wird durch Platzhalter ersetzt und im Manifest mit seinem Anzeigenamen
/// genannt. Der Import ersetzt die Platzhalter durch die Kennungen der Zielinstallation und
/// legt den Workflow als Stand an. <em>Veroeffentlicht wird nicht</em>: Wer ein fremdes Modell
/// in Betrieb nimmt, soll das bewusst tun.
/// </summary>
public sealed class ProcessPackageService(
    IStorageSystem storageSystem,
    ITransactionalStorageProvider storageProvider,
    DefinitionBusinessLogic definitionBusinessLogic,
    FormKeyResolver formKeyResolver)
{
    /// <summary>Das fertige Paket samt dem Dateinamen, unter dem es angeboten wird.</summary>
    public sealed record Package(byte[] Content, string FileName);

    #region Export

    public async Task<Package> ExportAsync(string definitionId)
    {
        BpmnMetaDefinition meta;
        try
        {
            meta = await storageSystem.DefinitionStorage.GetMetaDefinitionById(definitionId);
        }
        catch (DefinitionStorageNotFoundException)
        {
            throw new ProcessPackageException("package.workflow.not_found",
                $"Es gibt keinen Workflow mit der Kennung {definitionId}.");
        }

        var deployed = await storageSystem.DefinitionStorage.GetDeployedDefinition(definitionId);
        BpmnDefinition definition;
        string source;
        if (deployed is not null)
        {
            definition = deployed;
            source = ProcessPackageFormat.SourceDeployed;
        }
        else
        {
            try
            {
                definition = await storageSystem.DefinitionStorage.GetLatestDefinition(definitionId);
            }
            catch (Exception exception) when (exception is DefinitionStorageNotFoundException or FileNotFoundException)
            {
                throw new ProcessPackageException("package.workflow.no_version",
                    $"Der Workflow „{meta.Name}“ hat noch keinen gespeicherten Stand.", unprocessable: true);
            }

            source = ProcessPackageFormat.SourceDraft;
        }

        var xml = await storageSystem.DefinitionStorage.GetBinary(definition.Id);
        var scan = ProcessPackageReferences.Extract(xml, await LoadNamesAsync());
        var forms = await CollectFormsAsync(definition, xml);

        var manifest = new ProcessPackageManifestDto
        {
            Format = ProcessPackageFormat.FormatName,
            FormatVersion = ProcessPackageFormat.CurrentFormatVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            FlowzerVersion = FlowzerVersion,
            BpmnCapabilitiesContract = ContractVersionNumber,
            FormsContract = FormContract.ProfileV4,
            Workflow = new ProcessPackageWorkflowDto
            {
                DefinitionId = definitionId,
                Name = meta.Name,
                Description = meta.Description,
                Version = definition.Version.ToString(),
                ProcessIds = ProcessPackageBpmn.ProcessIds(xml),
                Source = source
            },
            Forms = forms.Select(form => form.Descriptor).ToArray(),
            References = scan.References.ToArray()
        };

        var content = ProcessPackageArchive.Write(
            manifest,
            scan.Xml,
            forms.ToDictionary(form => form.Descriptor.File, form => form.FormData, StringComparer.Ordinal),
            BuildReadme(manifest));

        return new Package(content, ProcessPackageFormat.FileName(meta.Name, definition.Version.ToString()));
    }

    private sealed record ExportedForm(ProcessPackageFormDto Descriptor, string FormData);

    /// <summary>
    /// Die Formularstaende der exportierten Fassung. Bei einer Veroeffentlichung sind das die
    /// unveraenderlichen Bindungen; bei einem Entwurf gibt es keine, dann wird genauso
    /// aufgeloest, wie eine Veroeffentlichung es taete.
    /// </summary>
    private async Task<List<ExportedForm>> CollectFormsAsync(BpmnDefinition definition, string xml)
    {
        List<ExportedForm> forms = [];
        var metadata = (await storageSystem.FormStorage.GetFormMetadatas()).ToArray();

        foreach (var formKey in ProcessPackageBpmn.FormKeys(xml))
        {
            var bound = definition.FormBindings?.GetValueOrDefault(formKey.Key);
            FormDto? form;
            if (bound is not null)
            {
                form = new FormDto
                {
                    Id = bound.Id,
                    FormId = bound.FormId,
                    FormData = bound.FormData,
                    Version = bound.Version is null ? null : ToDto(Version.FromString(bound.Version))
                };
            }
            else
            {
                // Ein Entwurf hat keine Bindung. Ein veroeffentlichter Altbestand kann ebenfalls
                // ohne Bindung dastehen; auch dann ist der aufloesbare Stand die ehrlichste
                // Auskunft, die das Paket geben kann.
                var resolved = await formKeyResolver.ResolveForDeploymentAsync(formKey.Key, definition.Id);
                if (resolved.Form is null)
                    throw new ProcessPackageException("package.form.unresolved",
                        $"Das Formular „{formKey.Key}“ ist nicht auflösbar: {resolved.ErrorMessage}",
                        unprocessable: true);
                form = resolved.Form;
            }

            var embedded = form.FormId is null;
            var name = embedded
                ? BPMN.Flowzer.FlowzerUserTaskForm.IdFromFormKey(formKey.Key) ?? formKey.Key
                : metadata.FirstOrDefault(item => item.FormId == form.FormId)?.Name ?? formKey.Key;

            forms.Add(new ExportedForm(
                new ProcessPackageFormDto
                {
                    FormId = form.FormId,
                    Name = name,
                    Revision = form.Version is null ? null : $"{form.Version.Major}.{form.Version.Minor}",
                    FormKey = formKey.Key,
                    File = ProcessPackageFormat.FormEntryPrefix + FormFileName(form.FormId, name, forms.Count + 1),
                    Embedded = embedded
                },
                form.FormData ?? "{}"));
        }

        return forms;
    }

    /// <summary>
    /// Der Dateiname eines Formulars im Paket. Die Formularkennung ist der beste Name, weil sie
    /// eindeutig ist; ein eingebettetes Formular hat keine und bekommt deshalb eine laufende
    /// Nummer statt eines Namens, der Sonderzeichen in den Eintragsnamen traegt.
    /// </summary>
    private static string FormFileName(Guid? formId, string name, int ordinal) =>
        formId is { } id ? $"{id}.json" : $"embedded-{ordinal:D2}.json";

    private async Task<ProcessPackageDirectoryNames> LoadNamesAsync()
    {
        Dictionary<Guid, string> users = new();
        Dictionary<Guid, string> groups = new();
        Dictionary<Guid, string> connections = new();

        // Beide Quellen sind optional: Eine Installation ohne Verzeichnis oder ohne
        // KI-Verbindungen soll trotzdem exportieren koennen. Fehlt ein Name, steht im Manifest
        // „(unbekannter Eintrag)“ — niemals die Kennung.
        try
        {
            if (await storageSystem.IdentityDirectoryStorage.GetActiveSnapshot() is { } snapshot)
            {
                foreach (var user in snapshot.Users) users[user.Id] = user.DisplayName;
                foreach (var group in snapshot.Groups) groups[group.Id] = group.Path;
            }
        }
        catch (NotSupportedException)
        {
            // Ablage ohne Identitaetsverzeichnis.
        }

        try
        {
            foreach (var connection in await storageSystem.AiConnectionStorage.List())
                connections[connection.Id] = connection.Name;
        }
        catch (NotSupportedException)
        {
            // Ablage ohne KI-Verbindungen.
        }

        return new ProcessPackageDirectoryNames(users, groups, connections);
    }

    private static string BuildReadme(ProcessPackageManifestDto manifest)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {manifest.Workflow.Name}");
        text.AppendLine();
        text.AppendLine($"Prozesspaket im Format `{manifest.Format}`, erzeugt am "
            + $"{manifest.ExportedAt:yyyy-MM-dd HH:mm} UTC von Flowzer {manifest.FlowzerVersion}.");
        text.AppendLine();
        text.AppendLine($"- Kennung: `{manifest.Workflow.DefinitionId}`");
        text.AppendLine($"- Fassung: {manifest.Workflow.Version} ("
            + (manifest.Workflow.Source == ProcessPackageFormat.SourceDeployed
                ? "veröffentlicht"
                : "gespeicherter Entwurf, nicht veröffentlicht") + ")");
        text.AppendLine($"- Prozesse: {string.Join(", ", manifest.Workflow.ProcessIds)}");
        text.AppendLine($"- Fähigkeitsvertrag: v{manifest.BpmnCapabilitiesContract}");
        text.AppendLine($"- Formularvertrag: {manifest.FormsContract}");
        text.AppendLine();

        text.AppendLine("## Formulare");
        text.AppendLine();
        if (manifest.Forms.Length == 0) text.AppendLine("Dieser Workflow verwendet keine Formulare.");
        foreach (var form in manifest.Forms)
            text.AppendLine($"- **{form.Name}**"
                + (form.Revision is null ? "" : $" · Fassung {form.Revision}")
                + (form.Embedded ? " · im Diagramm enthalten" : "")
                + $" · Schlüssel `{form.FormKey}`");
        text.AppendLine();

        text.AppendLine("## Beim Import zuzuordnen");
        text.AppendLine();
        var required = manifest.References.Where(reference => reference.RequiresMapping).ToArray();
        if (required.Length == 0) text.AppendLine("Nichts — dieser Workflow bindet keine Personen, Gruppen oder Verbindungen.");
        foreach (var reference in required)
            text.AppendLine($"- {KindLabel(reference.Kind)} „{reference.Label}“ an `{reference.ElementId}`");
        text.AppendLine();

        var notes = manifest.References.Where(reference => !reference.RequiresMapping).ToArray();
        if (notes.Length > 0)
        {
            text.AppendLine("## Was die Zielinstallation bereitstellen muss");
            text.AppendLine();
            foreach (var reference in notes)
                text.AppendLine($"- {KindLabel(reference.Kind)}: `{reference.Label}`");
            text.AppendLine();
        }

        text.AppendLine("Das Paket enthält keine Secrets, keine Instanzen, keine Aufgaben, keine Historie");
        text.AppendLine("und keine Personenkennungen.");
        return text.ToString();
    }

    private static string KindLabel(string kind) => kind switch
    {
        ProcessPackageReferenceKinds.DirectoryUser => "Person",
        ProcessPackageReferenceKinds.DirectoryGroup => "Gruppe",
        ProcessPackageReferenceKinds.AiConnection => "KI-Verbindung",
        ProcessPackageReferenceKinds.JobType => "Auftragstyp (Worker nötig)",
        ProcessPackageReferenceKinds.Secret => "Secret-Name",
        ProcessPackageReferenceKinds.CalledProcess => "Aufgerufener Prozess",
        ProcessPackageReferenceKinds.CalledDecision => "Aufgerufene Entscheidung",
        _ => kind
    };

    #endregion

    #region Vorschau

    public async Task<ProcessPackagePreviewDto> PreviewAsync(Stream package, FolderPermissions permissions)
    {
        var content = ProcessPackageArchive.Read(package);
        var manifest = content.Manifest;

        var issues = ProcessPackageBpmn.DeploymentIssues(content.WorkflowXml);
        var formsSupported = FormContract.IsSupportedProfile(manifest.FormsContract);

        List<ProcessPackageFindingDto> notices = [];
        if (manifest.Workflow.Source != ProcessPackageFormat.SourceDeployed)
            notices.Add(new ProcessPackageFindingDto
            {
                Code = "package.workflow.draft",
                Message = "Das Paket enthält einen gespeicherten Entwurf, keine veröffentlichte Fassung."
            });
        if (manifest.BpmnCapabilitiesContract != ContractVersionNumber)
            notices.Add(new ProcessPackageFindingDto
            {
                Code = "package.capabilities.other_version",
                Message = $"Das Paket wurde gegen Fähigkeitsvertrag v{manifest.BpmnCapabilitiesContract} erzeugt; "
                    + $"hier gilt v{ContractVersionNumber}."
            });
        if (!formsSupported)
            notices.Add(new ProcessPackageFindingDto
            {
                Code = "package.forms.unsupported_profile",
                Message = $"Das Formularprofil „{manifest.FormsContract}“ kennt diese Installation nicht."
            });

        return new ProcessPackagePreviewDto
        {
            Manifest = manifest,
            DeployableHere = issues.Count == 0,
            FormsContractSupported = formsSupported,
            Problems = issues.Select(issue => new ProcessPackageFindingDto
            {
                Code = issue.Code,
                Message = issue.Message,
                ElementId = issue.ElementId
            }).ToArray(),
            Notices = notices.ToArray(),
            References = await BuildOptionsAsync(manifest.References),
            Conflict = await FindConflictAsync(manifest.Workflow.DefinitionId, permissions)
        };
    }

    private async Task<ProcessPackageReferenceOptionsDto[]> BuildOptionsAsync(
        IReadOnlyList<ProcessPackageReferenceDto> references)
    {
        List<ProcessPackageCandidateDto> users = [];
        List<ProcessPackageCandidateDto> groups = [];
        List<ProcessPackageCandidateDto> connections = [];

        try
        {
            if (await storageSystem.IdentityDirectoryStorage.GetActiveSnapshot() is { } snapshot)
            {
                users = snapshot.Users.Where(user => user.IsActive)
                    .Select(user => new ProcessPackageCandidateDto
                        { Id = user.Id.ToString(), Label = user.DisplayName, Hint = user.Email ?? user.Username })
                    .OrderBy(candidate => candidate.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
                groups = snapshot.Groups.Where(group => group.IsActive)
                    .Select(group => new ProcessPackageCandidateDto
                        { Id = group.Id.ToString(), Label = group.Path, Hint = group.Name })
                    .OrderBy(candidate => candidate.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }
        catch (NotSupportedException)
        {
            // Ohne Verzeichnis gibt es keine Auswahl; die Vorschau nennt den Bezug trotzdem.
        }

        try
        {
            connections = (await storageSystem.AiConnectionStorage.List())
                .Where(connection => connection.Enabled)
                .Select(connection => new ProcessPackageCandidateDto
                    { Id = connection.Id.ToString(), Label = connection.Name, Hint = connection.DefaultModel })
                .OrderBy(candidate => candidate.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (NotSupportedException)
        {
            // Ohne KI-Verbindungen bleibt die Auswahl leer.
        }

        return references.Select(reference =>
        {
            var candidates = reference.Kind switch
            {
                ProcessPackageReferenceKinds.DirectoryUser => users,
                ProcessPackageReferenceKinds.DirectoryGroup => groups,
                ProcessPackageReferenceKinds.AiConnection => connections,
                _ => []
            };

            // Der Vorschlag ist der gleichnamige Eintrag. Er wird vorbelegt, aber nie
            // stillschweigend angewandt: Der Import verlangt die Zuordnung ausdruecklich.
            var suggestion = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Label, reference.Label, StringComparison.CurrentCultureIgnoreCase));

            return new ProcessPackageReferenceOptionsDto
            {
                Reference = reference,
                Candidates = candidates.ToArray(),
                SuggestedId = suggestion?.Id
            };
        }).ToArray();
    }

    private async Task<ProcessPackageConflictDto?> FindConflictAsync(string definitionId, FolderPermissions permissions)
    {
        var existing = (await storageSystem.DefinitionStorage.GetAllMetaDefinitions())
            .FirstOrDefault(meta => string.Equals(meta.DefinitionId, definitionId, StringComparison.Ordinal));
        if (existing is null) return null;

        return new ProcessPackageConflictDto
        {
            DefinitionId = existing.DefinitionId,
            Name = existing.Name,
            LatestVersion = existing.LatestVersion?.ToString(),
            MayCreateNewVersion = permissions.MayEditIn(existing.FolderId)
        };
    }

    #endregion

    #region Import

    public async Task<ProcessPackageImportResultDto> ImportAsync(
        Stream package,
        ProcessPackageMappingDto mapping,
        FolderPermissions permissions)
    {
        var content = ProcessPackageArchive.Read(package);
        var manifest = content.Manifest;

        var target = await ResolveTargetAsync(manifest, mapping, permissions);
        var xml = ProcessPackageReferences.Apply(content.WorkflowXml, mapping.References ?? []);
        xml = ProcessPackageBpmn.WithDefinitionId(xml, target.DefinitionId);

        var (forms, replacements) = await ImportFormsAsync(manifest, content.Forms);
        xml = ProcessPackageBpmn.WithFormKeys(xml, replacements);

        var createdMeta = false;
        if (target.CreateMeta)
        {
            await storageSystem.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = target.DefinitionId,
                Name = target.Name,
                Description = manifest.Workflow.Description,
                FolderId = target.FolderId
            });
            createdMeta = true;
        }

        BpmnDefinition definition;
        try
        {
            definition = await definitionBusinessLogic.StoreDefinition(xml, previousGuid: null);
        }
        catch
        {
            // Ein Katalogeintrag ohne jede Fassung waere im Katalog ein leerer Platzhalter, den
            // danach niemand zuordnen kann. Derselbe Aufraeumgedanke wie beim Anlegen.
            if (createdMeta) await TryRemoveMetaAsync(target.DefinitionId);
            throw;
        }

        List<ProcessPackageFindingDto> notices =
        [
            new()
            {
                Code = "package.import.not_deployed",
                Message = "Der Workflow ist angelegt, aber nicht veröffentlicht. "
                    + "Veröffentlichen Sie ihn im Modellierer, wenn er laufen soll."
            }
        ];
        foreach (var reference in manifest.References.Where(reference => !reference.RequiresMapping))
            notices.Add(new ProcessPackageFindingDto
            {
                Code = "package.reference.provide_locally",
                Message = $"{KindLabel(reference.Kind)}: „{reference.Label}“ muss diese Installation bereitstellen.",
                ElementId = string.IsNullOrEmpty(reference.ElementId) ? null : reference.ElementId
            });

        return new ProcessPackageImportResultDto
        {
            DefinitionId = target.DefinitionId,
            Name = target.Name,
            VersionId = definition.Id,
            Version = ToDto(definition.Version),
            Forms = forms.ToArray(),
            AppliedReferences = manifest.References.Where(reference => reference.RequiresMapping).ToArray(),
            Notices = notices.ToArray()
        };
    }

    private sealed record ImportTarget(string DefinitionId, string Name, Guid? FolderId, bool CreateMeta);

    private async Task<ImportTarget> ResolveTargetAsync(
        ProcessPackageManifestDto manifest,
        ProcessPackageMappingDto mapping,
        FolderPermissions permissions)
    {
        var all = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();

        if (string.Equals(mapping.Mode, ProcessPackageFormat.ModeNewVersionOf, StringComparison.Ordinal))
        {
            var definitionId = mapping.DefinitionId?.Trim();
            if (string.IsNullOrWhiteSpace(definitionId))
                throw new ProcessPackageException("package.import.definition_id_required",
                    "Für einen neuen Stand muss der vorhandene Workflow benannt werden.", unprocessable: true);

            var existing = all.FirstOrDefault(meta =>
                string.Equals(meta.DefinitionId, definitionId, StringComparison.Ordinal));
            if (existing is null)
                throw new ProcessPackageException("package.import.definition_not_found",
                    $"Es gibt keinen Workflow mit der Kennung {definitionId}.");

            if (!permissions.MayEditIn(existing.FolderId))
                throw new ProcessPackageException("package.import.forbidden",
                    existing.FolderId is null
                        ? "Workflows ausserhalb eines Ordners zu ändern ist der Rolle fürs Modellieren vorbehalten."
                        : "Für diesen Ordner fehlt Ihnen die Bearbeitungsberechtigung.");

            return new ImportTarget(existing.DefinitionId, existing.Name, existing.FolderId, CreateMeta: false);
        }

        if (!string.Equals(mapping.Mode, ProcessPackageFormat.ModeNew, StringComparison.Ordinal))
            throw new ProcessPackageException("package.import.mode_invalid",
                $"„{mapping.Mode}“ ist keine gültige Zielentscheidung; erlaubt sind "
                + $"„{ProcessPackageFormat.ModeNew}“ und „{ProcessPackageFormat.ModeNewVersionOf}“.",
                unprocessable: true);

        if (!FolderBusinessLogic.IsKnownTarget(mapping.FolderId, permissions.Folders))
            throw new ProcessPackageException("package.import.folder_not_found",
                $"Es gibt keinen Ordner mit der Kennung {mapping.FolderId}.");

        if (!permissions.MayEditIn(mapping.FolderId))
            throw new ProcessPackageException("package.import.forbidden",
                mapping.FolderId is null
                    ? "Workflows ausserhalb eines Ordners anzulegen ist der Rolle fürs Modellieren vorbehalten."
                    : "Für diesen Ordner fehlt Ihnen die Bearbeitungsberechtigung.");

        // Ohne gewuenschte Kennung wird die aus dem Paket genommen, solange sie frei ist. Sonst
        // eine neue: Zwei Workflows unter derselben Kennung waeren in der Ablage derselbe.
        var wanted = mapping.DefinitionId?.Trim();
        if (string.IsNullOrWhiteSpace(wanted))
            wanted = all.Any(meta => string.Equals(meta.DefinitionId, manifest.Workflow.DefinitionId, StringComparison.Ordinal))
                ? "definition_" + Guid.NewGuid()
                : manifest.Workflow.DefinitionId;

        if (!DefinitionIdRules.IsValid(wanted))
            throw new ProcessPackageException("package.import.definition_id_invalid",
                DefinitionIdRules.BuildErrorMessage(wanted), unprocessable: true);

        if (all.Any(meta => string.Equals(meta.DefinitionId, wanted, StringComparison.Ordinal)))
            throw new ProcessPackageException("package.import.definition_id_taken",
                $"Die Kennung {wanted} ist hier schon vergeben. Wählen Sie eine andere oder importieren "
                + "Sie als neuen Stand des vorhandenen Workflows.");

        var name = mapping.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = manifest.Workflow.Name;
        if (name.Length > MaxDefinitionNameLength) name = name[..MaxDefinitionNameLength];

        return new ImportTarget(wanted, name, mapping.FolderId, CreateMeta: true);
    }

    /// <summary>
    /// Legt die Formulare des Pakets an und liefert nebenbei, wie die Form-Keys danach lauten.
    ///
    /// Zugeordnet wird ausschliesslich ueber die stabile Formularkennung. Ueber den Namen zu
    /// gehen waere bequemer, machte aber aus einem gleichnamigen fremden Formular stillschweigend
    /// dasselbe — und ein Import schriebe dann in einen Bestand, den er gar nicht kennt.
    /// Gleiche Kennung und gleicher Inhalt heisst Wiederverwenden; gleiche Kennung und anderer
    /// Inhalt heisst neue Fassung; sonst entsteht ein neues Formular.
    /// </summary>
    private async Task<(List<ProcessPackageImportedFormDto> Forms, Dictionary<string, string> FormKeys)>
        ImportFormsAsync(ProcessPackageManifestDto manifest, IReadOnlyDictionary<string, string> files)
    {
        List<ProcessPackageImportedFormDto> imported = [];
        Dictionary<string, string> formKeys = new(StringComparer.Ordinal);
        if (manifest.Forms.Length == 0) return (imported, formKeys);

        using var storage = storageProvider.GetTransactionalStorage();
        var metadata = (await storage.FormStorage.GetFormMetadatas()).ToList();

        foreach (var descriptor in manifest.Forms)
        {
            if (descriptor.Embedded)
            {
                // Es liegt im Diagramm und reist dort mit; ein Katalogeintrag entstuende
                // sonst doppelt und ohne Besitzer.
                imported.Add(new ProcessPackageImportedFormDto
                {
                    FormKey = descriptor.FormKey, Name = descriptor.Name, Outcome = "embedded"
                });
                continue;
            }

            if (descriptor.FormId is not { } formId)
                throw new ProcessPackageException("package.form.id_missing",
                    $"Dem Formular „{descriptor.Name}“ im Manifest fehlt die Kennung.", unprocessable: true);

            if (!files.TryGetValue(descriptor.File, out var formData))
                throw new ProcessPackageException("package.form.file_missing",
                    $"Im Paket fehlt die Formulardatei „{descriptor.File}“.", unprocessable: true);

            var existing = metadata.FirstOrDefault(item => item.FormId == formId);
            string outcome;
            string name;

            if (existing is null)
            {
                name = UniqueName(descriptor.Name, metadata);
                await storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = name });
                metadata.Add(new FormMetadata { FormId = formId, Name = name });
                outcome = "created";
            }
            else
            {
                name = existing.Name;
                outcome = "reused";
            }

            var versions = (await storage.FormStorage.GetForms(formId)).ToArray();
            var identical = versions.FirstOrDefault(version =>
                string.Equals(NormalizeFormData(version.FormData), NormalizeFormData(formData), StringComparison.Ordinal));

            Version revision;
            if (identical is not null)
            {
                revision = identical.Version;
                if (outcome != "created") outcome = "reused";
            }
            else
            {
                revision = await storage.FormStorage.GetMaxVersion(formId) + 1;
                await storage.FormStorage.SaveForm(new Form
                {
                    Id = Guid.NewGuid(), FormId = formId, Version = revision, FormData = formData
                });
                if (outcome != "created") outcome = "revised";
            }

            imported.Add(new ProcessPackageImportedFormDto
            {
                FormKey = descriptor.FormKey,
                Name = name,
                FormId = formId,
                Revision = revision.ToString(),
                Outcome = outcome
            });

            // Der Schluessel zeigt danach auf genau den Stand, den das Paket mitgebracht hat.
            // Ohne die Fassung im Schluessel griffe spaeter „die neueste“ — und der importierte
            // Workflow liefe gegen ein Formular, das er nie gesehen hat.
            formKeys[descriptor.FormKey] = $"{name}:{revision}";
        }

        storage.CommitChanges();
        return (imported, formKeys);
    }

    /// <summary>
    /// Ein Formularname muss eindeutig bleiben: Der Form-Key loest ueber ihn auf, und zwei
    /// gleichnamige Formulare machen jede Aufloesung mehrdeutig.
    /// </summary>
    private static string UniqueName(string name, IReadOnlyCollection<FormMetadata> metadata)
    {
        if (metadata.All(item => !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            return name;

        for (var attempt = 2; attempt < 1000; attempt++)
        {
            var candidate = $"{name} ({attempt})";
            if (metadata.All(item => !string.Equals(item.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"{name} ({Guid.NewGuid():N})";
    }

    /// <summary>
    /// Vergleich zweier Formularstaende. Nur Zeilenenden und aeusserer Leerraum werden
    /// angeglichen — alles Weitere waere eine Auslegung des Schemas und koennte zwei wirklich
    /// verschiedene Formulare fuer gleich erklaeren.
    /// </summary>
    private static string NormalizeFormData(string formData) =>
        formData.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private async Task TryRemoveMetaAsync(string definitionId)
    {
        try
        {
            await storageSystem.DefinitionStorage.DeleteMetaDefinition(definitionId);
        }
        catch
        {
            // Best effort only: the original error is more relevant to the caller.
        }
    }

    #endregion

    /// <summary>Muss zur Grenze in <see cref="Controller.DefinitionController"/> passen.</summary>
    private const int MaxDefinitionNameLength = 200;

    private static VersionDto ToDto(Version version) => new() { Major = version.Major, Minor = version.Minor };

    /// <summary>Die Vertragsversion als Zahl; der Vertrag selbst fuehrt sie als Zeichenkette.</summary>
    private static int ContractVersionNumber =>
        int.TryParse(core_engine.BpmnCapabilityMatrix.Contract.ContractVersion, out var version) ? version : 0;

    private static string FlowzerVersion =>
        typeof(ProcessPackageService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
        ?? "0.0.0";
}
